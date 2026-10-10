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
using Overseer.Tests.Helpers;
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

    /// <summary>A stored chat consistency document over <see cref="ChatConsistencyReportTestData"/>, with its valid writer output.</summary>
    internal static BenchmarkReportDocument ChatConsistencyDocument(BenchmarkReportAudience audience)
    {
        var sheet = ChatConsistencyReportTestData.Sheet(audience);
        return new BenchmarkReportDocument
        {
            Id = 41,
            PackId = Guid.Parse("7c1e2d3f-4a5b-4c6d-8e9f-0a1b2c3d4e5f"),
            Audience = audience,
            Origin = BenchmarkReportDocumentOrigin.ChatConsistencyReport,
            Scope = BenchmarkReportScope.ChatConsistency,
            ChatConsistencyAnalysisId = 7,
            SubjectKey = sheet.SubjectKey,
            SubjectLabel = sheet.SubjectLabel,
            SubjectRunIdsJson = BenchmarkReportJson.Serialize(sheet.SubjectRunIds),
            ComparisonRequestJson = "{}",
            SuiteName = sheet.SuiteName,
            WriterConfigId = 3,
            WriterDisplayName = "Writer One",
            WriterProvider = "Anthropic",
            WriterModelId = "writer-1",
            ReportFormatVersion = BenchmarkReportPackRenderer.ChatConsistencyReportFormatVersion,
            WriterPromptSha256 = BenchmarkReportPackPrompt.PromptSha256(audience, BenchmarkReportScope.ChatConsistency),
            FactsJson = BenchmarkReportJson.Serialize(sheet),
            ContentJson = BenchmarkReportJson.Serialize(ChatConsistencyReportTestData.Content()),
            WriterOutputJson = BenchmarkReportJson.Serialize(ChatConsistencyReportTestData.ValidOutput(audience)),
            ValidationNotesJson = "[]",
            Title = BenchmarkReportPackRenderer.BuildChatConsistencyTitle(sheet),
            Status = BenchmarkReportDocumentStatus.Completed,
            CreatedAtUtc = CreatedAt
        };
    }

    [Fact]
    public void AChatConsistencyPdf_KeepsItsMinusSigns_ItsCoverFacts_AndItsAnalysisFooter_WithoutAHash()
    {
        var document = ChatConsistencyDocument(BenchmarkReportAudience.TechnicalReport);
        var options = new BenchmarkReportRenderOptions
        {
            Disclosure = BenchmarkReportDisclosure.Detailed,
            PeerNaming = BenchmarkReportPeerNaming.Anonymized
        };
        var info = BenchmarkPdfDocumentInfo.ForReportDocument(document, options, BenchmarkPdfPaper.A4);
        string markdown = BenchmarkReportPackRenderer.Render(document, options);
        Assert.Contains("\u22124.2\u00A0index points", markdown);

        byte[] pdf = BenchmarkPdfRenderer.RenderMarkdown(markdown, info, TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "Model", "Compared", "Baseline", "Comparison", "Controls", "Hours", "Protocol", "Analysis code", "Written", "Provenance" },
            info.Facts.Select(f => f.Label));
        Assert.Equal("Test Model (TestProvider, test-model-1)", info.Facts[0].Value);
        Assert.Equal("2026-09-01 00:00 UTC to 2026-09-08 00:00 UTC, 2 runs", info.Facts[2].Value);
        Assert.Equal("1 model (A), identity withheld", info.Facts[4].Value);
        Assert.Equal("weekdays 04–12 UTC", info.Facts[5].Value);
        Assert.Equal("V1, α 0.05", info.Facts[6].Value);
        Assert.Equal("version " + Overseer.Services.ChatConsistency.ChatConsistencyAnalysisService.CurrentAnalysisCodeVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), info.Facts[7].Value);
        Assert.Equal("2026-09-28 10:42 UTC by Writer One (Anthropic, writer-1)", info.Facts[8].Value);
        Assert.StartsWith("Figures and tables computed by Overseer", info.Facts[9].Value, StringComparison.Ordinal);
        Assert.Equal("Chat consistency analysis #7 · Report for AI Researchers and Developers", info.FooterText);

        string text = AllText(pdf);
        Assert.Contains("\u22124.2", text);
        Assert.DoesNotContain("-4.2", text);
        Assert.DoesNotContain("Source", text);
        Assert.DoesNotContain(info.SourceSha256.Length >= 16 ? info.SourceSha256[..16] : "0123456789abcdef", text);

        using var reader = PdfDocument.Open(pdf);
        int pages = reader.NumberOfPages;
        foreach (var page in reader.GetPages())
        {
            Assert.Contains(Squash("Chat consistency analysis #7 · Report for AI Researchers and Developers · page " + page.Number + " of " + pages), Squash(page.Text));
        }
    }

    [Fact]
    public void AnotherScopesPdf_KeepsItsSourceHashFooter()
    {
        var document = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.TechnicalReport);
        var options = new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Detailed, PeerNaming = BenchmarkReportPeerNaming.Anonymized };
        var info = BenchmarkPdfDocumentInfo.ForReportDocument(document, options, BenchmarkPdfPaper.A4);

        byte[] pdf = BenchmarkPdfRenderer.RenderMarkdown(BenchmarkReportPackRenderer.Render(document, options), info, TestContext.Current.CancellationToken);

        Assert.Null(info.FooterText);
        Assert.Contains("PDFlayout" + BenchmarkPdfRenderer.LayoutVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), AllText(pdf));
        Assert.Contains("Source", AllText(pdf));
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
    public void AWideTable_KeepsEveryHeaderWordWhole_AndRightAlignsItsNumericColumns()
    {
        const string wide =
            "| Question | Topic | Assessed band | Mean score | Critical errors | Refuted answer sentences | Tool calls | Model time |\n"
            + "|---|---|---|---|---|---|---|---|\n"
            + "| S1-Q1 | Throwing gems at unicorns, an item-identification-question | Intermediate | 90 | 0 | 0 | 2 | 8.1 s |\n"
            + "| S1-Q2 | Reading the map | Simple | 72 | 1 | 1 | 3 | 11.0 s |\n";
        string[] headers = { "Question", "Topic", "Assessed band", "Mean score", "Critical errors", "Refuted answer sentences", "Tool calls", "Model time" };

        var layout = BenchmarkPdfTableLayoutTests.Layout(wide);

        Assert.False(layout.Constant);
        Assert.True(layout.Fits);
        Assert.Equal(
            new[] { false, false, false, true, true, true, true, true },
            layout.Aligns.Select(a => a == BenchmarkPdfMarkdownComposer.CellAlign.Right).ToArray());

        // A header word in points as the composer estimates it: its characters by width class, 8 %
        // wider for the semibold header, at 5.2 points for 9.5 pt text scaled to the layout's text
        // size, plus the cell padding. Relative widths are spread across the A4 text width first.
        double scale = 482 / layout.Widths.Sum(w => (double)w);
        double characterPoints = 5.2 * layout.FontSize / 9.5;
        for (int g = 0; g < layout.GridColumns.Length; g++)
        {
            int c = layout.GridColumns[g];
            string header = layout.HeaderTextOf(c) ?? headers[c];
            double needed = BenchmarkPdfMarkdownComposer.Words(header)
                .Max(w => BenchmarkPdfMarkdownComposer.WordCharacters(w) * 1.08) * characterPoints + 8;
            double width = layout.Widths[g] * scale;
            Assert.True(width >= needed - 0.001,
                $"Column '{header}' is {width:0.0} points at {layout.FontSize} pt, narrower than its longest header word ({needed:0.0}).");
        }
    }

    [Fact]
    public void AShortTable_StartsAndEndsOnOnePage_WhereverThePageBreakFalls()
    {
        bool movedPastPageOne = false;
        for (int filler = 14; filler <= 60; filler += 2)
        {
            var markdown = new StringBuilder();
            for (int i = 1; i <= filler; i++)
            {
                markdown.Append("Filler paragraph ").Append(i).Append(" moves the table down the page.\n\n");
            }
            markdown.Append("| TABLEHEADMARKER | Value |\n|---|---|\n");
            for (int row = 1; row <= 4; row++)
            {
                markdown.Append("| Row ").Append(row).Append(" of a short table | ").Append(row * 7).Append(" |\n");
            }
            markdown.Append("| LASTROWMARKER | 99 |\n");

            byte[] pdf = BenchmarkPdfRenderer.RenderMarkdown(markdown.ToString(), Info(), TestContext.Current.CancellationToken);

            using var reader = PdfDocument.Open(pdf);
            var pages = reader.GetPages().ToList();
            int head = pages.First(p => Squash(p.Text).Contains("TABLEHEADMARKER", StringComparison.Ordinal)).Number;
            int last = pages.First(p => Squash(p.Text).Contains("LASTROWMARKER", StringComparison.Ordinal)).Number;
            Assert.True(head == last, $"With {filler} filler paragraphs the table starts on page {head} and ends on page {last}.");
            movedPastPageOne |= head > 1;
        }

        Assert.True(movedPastPageOne, "No filler count pushed the table past page 1, so no page break fell inside it.");
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
            new[] { "Document ID", "Disclosure", "Compared with", "Pricing basis", "Suite", "Questions", "Run", "Created (UTC)", "Generated format", "Writer", "Provenance" },
            info.Facts.Select(f => f.Label));
        Assert.Equal("101", info.Facts.Single(f => f.Label == "Document ID").Value);
        Assert.Equal("2 models (A and B), identities withheld", info.Facts.Single(f => f.Label == "Compared with").Value);
        Assert.Equal("Catalog prices on 2026-09-20 (price card dated 2026-09-01)", info.Facts.Single(f => f.Label == "Pricing basis").Value);
        Assert.Equal("4", info.Facts.Single(f => f.Label == "Questions").Value);
        Assert.Equal("version " + BenchmarkReportPackRenderer.ReportFormatVersion, info.Facts.Single(f => f.Label == "Generated format").Value);
        Assert.Equal("Claude Opus 5.5 (Anthropic, claude-opus-5-5; high)", info.Facts.Single(f => f.Label == "Writer").Value);
        Assert.Equal("Figures and tables computed by Overseer; prose written by the writer and checked automatically for structure, "
            + "permitted figures and references, word limits and disclosure. The checks do not verify its interpretations.",
            info.Facts.Single(f => f.Label == "Provenance").Value);

        var noThinking = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.TechnicalReport);
        noThinking.WriterThinkingLevel = null;
        Assert.Equal("Claude Opus 5.5 (Anthropic, claude-opus-5-5)", BenchmarkPdfDocumentInfo.ForReportDocument(noThinking, options, BenchmarkPdfPaper.A4)
            .Facts.Single(f => f.Label == "Writer").Value);

        var standalone = BenchmarkPdfDocumentInfo.ForReportDocument(
            BenchmarkReportPackFixture.StandaloneDocument(BenchmarkReportAudience.ExecutiveSummary),
            new BenchmarkReportRenderOptions { Disclosure = BenchmarkReportDisclosure.Full, PeerNaming = BenchmarkReportPeerNaming.Named },
            BenchmarkPdfPaper.A4);
        Assert.Equal("none (stand-alone report)", standalone.Facts.Single(f => f.Label == "Peers").Value);
        Assert.Equal("INTERNAL — unpublished benchmark results. Do not share outside the Overseer team.", standalone.ClassificationText);

        var older = BenchmarkReportPackFixture.StoredV2Document(BenchmarkReportAudience.TechnicalReport);
        Assert.Equal("version 2", BenchmarkPdfDocumentInfo.ForReportDocument(older, options, BenchmarkPdfPaper.A4)
            .Facts.Single(f => f.Label == "Generated format").Value);
    }

    [Fact]
    public void AReportDocumentPdf_PrintsItsCoverFacts_AndTheLayoutVersion()
    {
        var document = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.TechnicalReport);
        var options = new BenchmarkReportRenderOptions
        {
            Disclosure = BenchmarkReportDisclosure.Full,
            PeerNaming = BenchmarkReportPeerNaming.Named,
            IncludeFrontMatter = false,
            IncludeDocumentFooter = false
        };
        var info = BenchmarkPdfDocumentInfo.ForReportDocument(document, options, BenchmarkPdfPaper.A4);

        byte[] pdf = BenchmarkPdfRenderer.RenderMarkdown(BenchmarkReportPackRenderer.Render(document, options), info, TestContext.Current.CancellationToken);

        string text = AllText(pdf);
        Assert.Contains(Squash("Document ID"), text);
        Assert.Contains(Squash("Questions"), text);
        Assert.Contains(Squash("Generated format"), text);
        Assert.Contains(Squash("Provenance"), text);
        // The Markdown footer is left out; the cover states its facts once.
        Assert.DoesNotContain(Squash("Figures and tables were computed by Overseer."), text);
        Assert.DoesNotContain(Squash("rendered with format version"), text);
        Assert.Contains(Squash("PDF layout 8"), text);
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

    // --- Figures -----------------------------------------------------------------------------------

    private const string FigureMarkdown =
        "## Results\n\nIntro text.\n\n[[figure:p1a-quality]]\n\nMiddle text.\n\n[[figure:s1-quality-speed]]\n\n"
        + "[[figure:p2-profile]]\n\n[[not a figure]] stays.\n\nEnd text.\n";

    [Fact]
    public void TwoCharts_AreDrawnAsTaggedFigures_WithNumberedCaptions()
    {
        var charts = TwoCharts();

        byte[] withCharts = BenchmarkPdfRenderer.RenderMarkdown(FigureMarkdown, Info(), TestContext.Current.CancellationToken, charts);
        byte[] without = BenchmarkPdfRenderer.RenderMarkdown(FigureMarkdown, Info(), TestContext.Current.CancellationToken);

        // The frame draws one image per page: the logo on page 1 and the emblem on every later page.
        var (withImages, withPages) = ImageCount(withCharts);
        var (withoutImages, withoutPages) = ImageCount(without);
        Assert.Equal(withoutPages, withoutImages);
        Assert.Equal(withPages + 2, withImages);

        using var reader = PdfDocument.Open(withCharts);
        var elements = StructureElements(reader);
        Assert.Contains(elements, e => e.Type == "Figure" && e.Alt == charts[0].AltText);
        Assert.Contains(elements, e => e.Type == "Figure" && e.Alt == charts[1].AltText);
        Assert.Equal(2, elements.Count(e => e.Type == "Caption"));

        string text = AllText(withCharts);
        Assert.Contains(Squash("Figure 1. Quality index — Higher is better."), text);
        Assert.Contains(Squash("Figure 2. Quality against speed — Up and left is better."), text);
        Assert.True(text.IndexOf("Figure1.", StringComparison.Ordinal) < text.IndexOf("Figure2.", StringComparison.Ordinal));
        // A marker without a chart prints nothing; other double-bracket text prints literally.
        Assert.DoesNotContain("[[figure:", text);
        Assert.Contains(Squash("[[not a figure]] stays."), text);

        string plain = AllText(without);
        Assert.DoesNotContain("Figure1.", plain);
        Assert.DoesNotContain("[[figure:", plain);
    }

    [Fact]
    public void APdfWithFigures_StillDeclaresPdfA3AndPdfUA1()
    {
        byte[] pdf = BenchmarkPdfRenderer.RenderMarkdown(FigureMarkdown, Info(), TestContext.Current.CancellationToken, TwoCharts());

        using var reader = PdfDocument.Open(pdf);
        Assert.True(reader.TryGetXmpMetadata(out var xmp), "The PDF carries no XMP metadata.");
        var xml = xmp.GetXDocument();
        Assert.True(HasXmpValue(xml, "http://www.aiim.org/pdfa/ns/id/", "part", "3"), "pdfaid:part is not 3.");
        Assert.True(HasXmpValue(xml, "http://www.aiim.org/pdfua/ns/id/", "part", "1"), "pdfuaid:part is not 1.");
    }

    [Fact]
    public void TheSourceHash_CoversTheDrawnCharts()
    {
        var charts = TwoCharts();
        var changed = new[] { charts[0], charts[1] with { Png = TestPngs.Make(900, 500, 200) } };
        changed[1] = changed[1] with { Sha256 = BenchmarkReportChartStore.Sha256Hex(changed[1].Png) };

        string original = BenchmarkPdfRenderer.SourceSha256(FigureMarkdown, charts);
        string altered = BenchmarkPdfRenderer.SourceSha256(FigureMarkdown, changed);
        Assert.NotEqual(original, altered);
        Assert.Equal(BenchmarkPdfRenderer.Sha256(FigureMarkdown), BenchmarkPdfRenderer.SourceSha256(FigureMarkdown, null));

        string first = AllText(BenchmarkPdfRenderer.RenderMarkdown(FigureMarkdown, Info(), TestContext.Current.CancellationToken, charts));
        string second = AllText(BenchmarkPdfRenderer.RenderMarkdown(FigureMarkdown, Info(), TestContext.Current.CancellationToken, changed));
        Assert.Contains("Source" + original[..16], first);
        Assert.Contains("Source" + altered[..16], second);
        Assert.Contains("PDFlayout8", first);

        // A chart with no marker is not drawn and leaves the hash alone.
        string unplaced = AllText(BenchmarkPdfRenderer.RenderMarkdown("Only text.\n", Info(), TestContext.Current.CancellationToken, charts));
        Assert.Contains("Source" + BenchmarkPdfRenderer.Sha256("Only text.\n")[..16], unplaced);
    }

    [Fact]
    public void ATallChart_IsCappedAtSixtyPercentOfTheContentHeight()
    {
        var frame = BenchmarkPdfRenderer.FigureFrameFor(BenchmarkPdfPaper.A4);
        var (wideWidth, wideHeight) = BenchmarkPdfMarkdownComposer.FigureSize(1000, 500, frame.Width, frame.MaxHeight);
        Assert.Equal((double)frame.Width, wideWidth, 0.001);
        Assert.Equal(frame.Width / 2.0, wideHeight, 0.001);

        var (tallWidth, tallHeight) = BenchmarkPdfMarkdownComposer.FigureSize(400, 2000, frame.Width, frame.MaxHeight);
        Assert.Equal((double)frame.MaxHeight, tallHeight, 0.001);
        Assert.Equal(frame.MaxHeight / 5.0, tallWidth, 0.001);

        // 60 % of A4's height between the 18 mm top and bottom margins.
        Assert.Equal((841.89 - 2 * 18 * 72 / 25.4) * 0.6, frame.MaxHeight, 0.1);

        byte[] pdf = BenchmarkPdfRenderer.RenderMarkdown(
            "[[figure:p2-profile]]\n",
            Info(),
            TestContext.Current.CancellationToken,
            new[] { Chart("p2-profile", 400, 2000, 60, "Tall", "A tall chart.", "Tall chart alt text.") });
        using var reader = PdfDocument.Open(pdf);
        Assert.Contains(StructureElements(reader), e => e.Type == "Figure" && e.Alt == "Tall chart alt text.");
    }

    // --- Figure layout -------------------------------------------------------------------------------

    private const string RowMarkdown = "## Results\n\nIntro text.\n\n[[figure:p1a-quality]]\n\n[[figure:p1b-speed]]\n\nEnd text.\n";

    private static BenchmarkReportRenderChart[] RowCharts() => new[]
    {
        Chart("p1a-quality", 800, 450, 10, "Quality index", "Left chart.", "Bar chart of the quality index."),
        Chart("p1b-speed", 800, 450, 90, "Answer time", "Right chart.", "Bar chart of the answer time.")
    };

    private static BenchmarkReportChartLayout Layout(double? maxHeightShare, params (string Key, double Width, int? Row)[] figures) => new()
    {
        Figures = figures.Select(f => new BenchmarkReportChartLayoutFigure { Key = f.Key, WidthShare = f.Width, RowGroup = f.Row }).ToList(),
        MaxHeightShare = maxHeightShare
    };

    /// <summary>The chart images of a PDF, wider or taller than the frame's logo and emblem, with their page numbers.</summary>
    private static List<(int Page, UglyToad.PdfPig.Core.PdfRectangle Bounds)> ChartImages(byte[] pdf)
    {
        using var reader = PdfDocument.Open(pdf);
        return reader.GetPages()
            .SelectMany(p => p.GetImages().Select(i => (Page: p.Number, Bounds: i.BoundingBox)))
            .Where(i => i.Bounds.Width > 150 || i.Bounds.Height > 100)
            .ToList();
    }

    [Fact]
    public void TwoFiguresOfOneRowGroup_PrintSideBySide_EachWithItsOwnCaption()
    {
        byte[] pdf = BenchmarkPdfRenderer.RenderMarkdown(
            RowMarkdown, Info(), TestContext.Current.CancellationToken, RowCharts(),
            Layout(null, ("p1a-quality", 0.5, 1), ("p1b-speed", 0.5, 1)));

        var images = ChartImages(pdf).OrderBy(i => i.Bounds.Left).ToList();
        Assert.Equal(2, images.Count);
        Assert.Equal(images[0].Page, images[1].Page);
        Assert.Equal(images[0].Bounds.Top, images[1].Bounds.Top, 1.0);
        Assert.True(images[1].Bounds.Left >= images[0].Bounds.Right + BenchmarkPdfMarkdownComposer.FigureRowGap - 1,
            $"The second figure starts at {images[1].Bounds.Left}, the first ends at {images[0].Bounds.Right}.");

        // Each cell is half the column less half the gap; the image fills its cell's width.
        var frame = BenchmarkPdfRenderer.FigureFrameFor(BenchmarkPdfPaper.A4);
        double cell = (frame.Width - BenchmarkPdfMarkdownComposer.FigureRowGap) / 2;
        Assert.All(images, i => Assert.Equal(cell, i.Bounds.Width, 1.0));

        using var reader = PdfDocument.Open(pdf);
        var elements = StructureElements(reader);
        // The cover's logo is a figure too; count the charts by their alt text.
        Assert.Equal(2, elements.Count(e => e.Type == "Figure" && e.Alt?.StartsWith("Bar chart", StringComparison.Ordinal) == true));
        Assert.Equal(2, elements.Count(e => e.Type == "Caption"));
        string text = AllText(pdf);
        Assert.Contains(Squash("Figure 1. Quality index — Left chart."), text);
        Assert.Contains(Squash("Figure 2. Answer time — Right chart."), text);

        // Without the layout the same figures print one under the other, each across the column.
        var stacked = ChartImages(BenchmarkPdfRenderer.RenderMarkdown(RowMarkdown, Info(), TestContext.Current.CancellationToken, RowCharts()));
        Assert.Equal(2, stacked.Count);
        Assert.True(stacked.Select(i => (i.Page, Math.Round(i.Bounds.Top))).Distinct().Count() == 2, "The figures without a layout share a line.");
        Assert.All(stacked, i => Assert.Equal((double)frame.Width, i.Bounds.Width, 1.0));
    }

    [Fact]
    public void FiguresOfDifferentRowGroups_OrWithTextBetweenThem_PrintAlone()
    {
        var prepared = BenchmarkPdfMarkdownComposer.Prepare(
            "[[figure:p1a-quality]]\n\n[[figure:p1b-speed]]\n\n[[figure:p1c-cost]]\n\nText.\n\n[[figure:p2-profile]]\n\n"
            + "[[figure:s1-quality-speed]]\n\n[[figure:s2-quality-cost]]\n\n[[figure:s3-speed-cost]]\n",
            "Title",
            new[] { "p1a-quality", "p1b-speed", "p1c-cost", "p2-profile", "s1-quality-speed", "s2-quality-cost", "s3-speed-cost" }
                .Select((key, i) => Chart(key, 640, 360, (byte)(i * 30), key, string.Empty, "Alt " + key))
                .ToArray(),
            Layout(null,
                ("p1a-quality", 0.5, 1), ("p1b-speed", 0.5, 1), ("p1c-cost", 0.5, 1),
                ("p2-profile", 0.5, 2),
                ("s1-quality-speed", 0.5, 3), ("s2-quality-cost", 0.5, 4), ("s3-speed-cost", 0.6667, null)));

        // Three of one group: the first two pair and the third prints alone; text ends a group; groups differ.
        var rows = prepared.FigureRows.Values.Select(r => string.Join(",", r.Select(f => f.Chart.FigureKey))).ToList();
        Assert.Equal(new[] { "p1a-quality,p1b-speed" }, rows);
        Assert.Single(prepared.RowFollowers);
        Assert.Equal(0.6667, prepared.Figures.Values.Single(f => f.Chart.FigureKey == "s3-speed-cost").WidthShare, 4);
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7 }, prepared.OrderedFigures.Select(f => f.Number));
    }

    [Fact]
    public void AFigureOfHalfTheColumn_IsHalfAsWide_AndCentered()
    {
        var chart = Chart("p1a-quality", 800, 450, 10, "Quality index", "Higher is better.", "Bar chart of the quality index.");

        byte[] pdf = BenchmarkPdfRenderer.RenderMarkdown(
            "[[figure:p1a-quality]]\n", Info(), TestContext.Current.CancellationToken, new[] { chart },
            Layout(null, ("p1a-quality", 0.5, null)));

        var image = Assert.Single(ChartImages(pdf)).Bounds;
        var frame = BenchmarkPdfRenderer.FigureFrameFor(BenchmarkPdfPaper.A4);
        Assert.Equal(frame.Width * 0.5, image.Width, 1.0);
        // A4 is 595.28 points wide, with equal side margins.
        Assert.Equal(595.28 / 2, image.Left + image.Width / 2, 1.0);
        Assert.Contains(Squash("Figure 1. Quality index — Higher is better."), AllText(pdf));
    }

    [Fact]
    public void ALayoutsMaximumHeightShare_ReplacesTheSixtyPercentCap()
    {
        Assert.Equal(0.6, BenchmarkPdfMarkdownComposer.MaxHeightShareOf(null));
        Assert.Equal(0.6, BenchmarkPdfMarkdownComposer.MaxHeightShareOf(new BenchmarkReportChartLayout()));
        Assert.Equal(0.4, BenchmarkPdfMarkdownComposer.MaxHeightShareOf(new BenchmarkReportChartLayout { MaxHeightShare = 0.4 }));

        var frame = BenchmarkPdfRenderer.FigureFrameFor(BenchmarkPdfPaper.A4, 0.4);
        Assert.Equal((841.89 - 2 * 18 * 72 / 25.4) * 0.4, frame.MaxHeight, 0.1);
        Assert.Equal(BenchmarkPdfRenderer.FigureFrameFor(BenchmarkPdfPaper.A4).Width, frame.Width);

        var tall = Chart("p2-profile", 400, 2000, 60, "Tall", "A tall chart.", "Tall chart alt text.");
        byte[] pdf = BenchmarkPdfRenderer.RenderMarkdown(
            "[[figure:p2-profile]]\n", Info(), TestContext.Current.CancellationToken, new[] { tall },
            new BenchmarkReportChartLayout { MaxHeightShare = 0.4 });

        var image = Assert.Single(ChartImages(pdf)).Bounds;
        Assert.Equal((double)frame.MaxHeight, image.Height, 1.0);
        Assert.Equal(frame.MaxHeight / 5.0, image.Width, 1.0);
    }

    [Fact]
    public void AFigureRowAfterAHeading_StartsOnTheHeadingsPage()
    {
        var charts = RowCharts();
        var layout = Layout(null, ("p1a-quality", 0.5, 1), ("p1b-speed", 0.5, 1));
        bool movedPastPageOne = false;
        for (int filler = 10; filler <= 40; filler += 3)
        {
            var markdown = new StringBuilder("## Opening\n\n");
            for (int i = 1; i <= filler; i++)
            {
                markdown.Append("Filler paragraph ").Append(i).Append(" moves the heading down the page.\n\n");
            }
            markdown.Append("## ROWHEADING\n\n[[figure:p1a-quality]]\n\n[[figure:p1b-speed]]\n\nAfter the row.\n");

            byte[] pdf = BenchmarkPdfRenderer.RenderMarkdown(markdown.ToString(), Info(), TestContext.Current.CancellationToken, charts, layout);

            using var reader = PdfDocument.Open(pdf);
            int heading = reader.GetPages().First(p => Squash(p.Text).Contains("ROWHEADING", StringComparison.Ordinal)).Number;
            var images = ChartImages(pdf);
            Assert.Equal(2, images.Count);
            Assert.All(images, i => Assert.True(i.Page == heading, $"With {filler} filler paragraphs the heading is on page {heading} and a figure on page {i.Page}."));
            movedPastPageOne |= heading > 1;
        }

        Assert.True(movedPastPageOne, "No filler count pushed the heading past page 1.");
    }

    // --- The last section ----------------------------------------------------------------------------

    [Fact]
    public void TheLayoutVersion_IsEight()
    {
        Assert.Equal(8, BenchmarkPdfRenderer.LayoutVersion);
    }

    [Fact]
    public void ABoldLabelBeforeATable_MovesWithTheTable_SoItNeverStandsAloneAtThePageFoot()
    {
        // Filler pushes the label, alone and under a heading, down the page at every position near its foot
        // in turn; the short table after it is kept on one page.
        foreach (string lead in new[] { string.Empty, "## Verdicts by endpoint\n\n" })
        {
            for (int filler = 30; filler <= 58; filler += 2)
            {
                var sb = new StringBuilder("# Heading\n\n");
                for (int i = 0; i < filler; i++) sb.Append("Filler paragraph ").Append(i).Append(" with some words to take up a line.\n\n");
                sb.Append(lead).Append("**WHERESTANDSLABEL**\n\n| Measure | Baseline | Comparison |\n|---|---|---|\n");
                for (int i = 0; i < 6; i++) sb.Append("| ROW").Append(i + 1).Append(" | 82.0 | 81.7 |\n");

                byte[] pdf = BenchmarkPdfRenderer.RenderMarkdown(sb.ToString(), Info(), TestContext.Current.CancellationToken);

                using var reader = PdfDocument.Open(pdf);
                var labelPage = reader.GetPages().Last(p => Squash(p.Text).Contains("WHERESTANDSLABEL", StringComparison.Ordinal));
                Assert.True(Squash(labelPage.Text).Contains("ROW182.081.7", StringComparison.Ordinal),
                    "With " + filler + " filler paragraphs" + (lead.Length > 0 ? " and a heading" : string.Empty) + " the label stands on a page without its table.");
            }
        }

        Assert.True(BenchmarkPdfMarkdownComposer.IsBoldHeadingParagraph(
            BenchmarkPdfMarkdownComposer.Parse("**Where the chat stands**").OfType<Markdig.Syntax.ParagraphBlock>().Single()));
        Assert.False(BenchmarkPdfMarkdownComposer.IsBoldHeadingParagraph(
            BenchmarkPdfMarkdownComposer.Parse("**Where the change came from.** The analysis attributes it.").OfType<Markdig.Syntax.ParagraphBlock>().Single()));
    }

    [Fact]
    public void AHeadingBeforeAShortTable_MovesWithTheTable_SoItNeverStandsAloneAtThePageFoot()
    {
        // Filler pushes the heading down the page, at every position near its foot in turn; the short
        // table after it is kept on one page.
        for (int filler = 30; filler <= 56; filler += 2)
        {
            var sb = new StringBuilder("# Heading\n\n");
            for (int i = 0; i < filler; i++) sb.Append("Filler paragraph ").Append(i).Append(" with some words to take up a line.\n\n");
            sb.Append("## Overseer events\n\n| Event | When | What changed |\n|---|---|---|\n");
            for (int i = 0; i < 8; i++) sb.Append("| E").Append(i + 1).Append(" | 2026-10-08 | harness ").Append(i).Append(" |\n");

            byte[] pdf = BenchmarkPdfRenderer.RenderMarkdown(sb.ToString(), Info(), TestContext.Current.CancellationToken);

            using var reader = PdfDocument.Open(pdf);
            var headingPage = reader.GetPages().Last(p => Squash(p.Text).Contains(Squash("Overseer events"), StringComparison.Ordinal));
            Assert.True(Squash(headingPage.Text).Contains("E12026-10-08", StringComparison.Ordinal),
                "With " + filler + " filler paragraphs the heading stands on a page without its table.");
        }
    }

    [Fact]
    public void AComparisonDocument_PrintsTheComparisonInItsRunningHeader_OnOneLine()
    {
        const string name = "Luna vs Grok vs Mistral";
        var document = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.TechnicalReport);
        document.ComparisonId = 12;
        document.Comparison = new BenchmarkComparison
        {
            Id = 12,
            ComparisonKey = new string('c', 64),
            EntryKeysJson = "[]",
            SubjectKind = BenchmarkComparisonSubjectKind.Runs,
            EntryCount = 3,
            DefaultName = name,
            CreatedAtUtc = CreatedAt
        };
        var options = new BenchmarkReportRenderOptions
        {
            Disclosure = BenchmarkReportDisclosure.Full,
            PeerNaming = BenchmarkReportPeerNaming.Named,
            IncludeFrontMatter = false,
            IncludeDocumentFooter = false
        };

        var info = BenchmarkPdfDocumentInfo.ForReportDocument(document, options, BenchmarkPdfPaper.A4);
        using (var reader = PdfDocument.Open(BenchmarkPdfRenderer.RenderMarkdown(
                   BenchmarkReportPackRenderer.Render(document, options), info, TestContext.Current.CancellationToken)))
        {
            Assert.True(reader.NumberOfPages >= 2, "The report should run past one page.");
            Assert.Contains(Squash("Comparison #12 — " + name), Squash(reader.GetPage(2).Text));
            Assert.Contains(Squash("Comparison #12 — " + name + " · 3 models · computed 2026-09-20"), Squash(reader.GetPage(1).Text));
        }

        // A name wider than the header is cut on one line; the cover still prints it whole.
        string longName = string.Join(" ", Enumerable.Repeat("Averyverylongcomparisonname", 12));
        document.Comparison!.Name = longName;
        var longInfo = BenchmarkPdfDocumentInfo.ForReportDocument(document, options, BenchmarkPdfPaper.A4);
        using (var reader = PdfDocument.Open(BenchmarkPdfRenderer.RenderMarkdown(
                   BenchmarkReportPackRenderer.Render(document, options), longInfo, TestContext.Current.CancellationToken)))
        {
            string header = Squash(reader.GetPage(2).Text);
            Assert.Contains(Squash("Comparison #12 — Averyverylongcomparisonname"), header);
            Assert.DoesNotContain(Squash(longName), header);
            Assert.Contains(Squash(longName), Squash(reader.GetPage(1).Text));
        }
    }

    private const string ClosingSection =
        "## Evaluation terms\n\n"
        + "- **Purpose statement:** Internal evaluation of candidate AI models for the Overseer assistant within GnollHack.\n"
        + "- **Distillation / training prohibition:** No prompt, completion, or evaluation output in this benchmark is used for model training.\n"
        + "- **Third-party model content:** Outputs generated by the model under test, graded by models from two providers, are third-party content.\n"
        + "- A fourth item that takes a line.\n"
        + "- A fifth item that takes a line.\n"
        + "- A sixth item that takes a line.\n"
        + "- A seventh item that takes a line.\n"
        + "- CLOSINGMARKER is the section's last line.\n";

    [Fact]
    public void AShortLastSection_IsKeptTogether_AndATallOneOrAnOnlySectionIsNot()
    {
        var frame = BenchmarkPdfRenderer.FigureFrameFor(BenchmarkPdfPaper.A4);

        var brief = BenchmarkPdfMarkdownComposer.Prepare("## Results\n\nText.\n\n" + ClosingSection, "Title");
        Assert.Equal(2, BenchmarkPdfMarkdownComposer.KeptTogetherSectionStart(brief, frame));
        Assert.True(frame.PageHeight > 700, "A4's height between the margins is " + frame.PageHeight + " points.");
        Assert.Null(BenchmarkPdfMarkdownComposer.KeptTogetherSectionStart(brief, frame with { PageHeight = 0 }));

        var tall = new StringBuilder("## Results\n\nText.\n\n## Question details\n\n");
        for (int i = 1; i <= 60; i++)
        {
            tall.Append("Paragraph ").Append(i).Append(" of a section taller than a page.\n\n");
        }
        Assert.Null(BenchmarkPdfMarkdownComposer.KeptTogetherSectionStart(BenchmarkPdfMarkdownComposer.Prepare(tall.ToString(), "Title"), frame));

        Assert.Null(BenchmarkPdfMarkdownComposer.KeptTogetherSectionStart(BenchmarkPdfMarkdownComposer.Prepare(ClosingSection, "Title"), frame));
    }

    [Fact]
    public void AShortLastSection_StartsAndEndsOnOnePage_WhereverThePageBreakFalls()
    {
        bool movedPastPageOne = false;
        for (int filler = 14; filler <= 44; filler += 2)
        {
            var markdown = new StringBuilder("## Results\n\n");
            for (int i = 1; i <= filler; i++)
            {
                markdown.Append("Filler paragraph ").Append(i).Append(" moves the last section down the page.\n\n");
            }
            markdown.Append(ClosingSection);

            byte[] pdf = BenchmarkPdfRenderer.RenderMarkdown(markdown.ToString(), Info(), TestContext.Current.CancellationToken);

            using var reader = PdfDocument.Open(pdf);
            var pages = reader.GetPages().ToList();
            int heading = pages.First(p => Squash(p.Text).Contains("Evaluationterms", StringComparison.Ordinal)).Number;
            int last = pages.First(p => Squash(p.Text).Contains("CLOSINGMARKER", StringComparison.Ordinal)).Number;
            Assert.True(heading == last, $"With {filler} filler paragraphs the section starts on page {heading} and ends on page {last}.");
            movedPastPageOne |= heading > 1;
        }

        Assert.True(movedPastPageOne, "No filler count pushed the section past page 1, so no page break fell inside it.");
    }

    [Fact]
    public void ALastSectionTallerThanAPage_FlowsAcrossPages()
    {
        var markdown = new StringBuilder("## Results\n\nText.\n\n## Question details\n\n");
        for (int i = 1; i <= 120; i++)
        {
            markdown.Append("Detail paragraph ").Append(i).Append(" of a section that no page can hold.\n\n");
        }
        markdown.Append("TALLMARKER ends the section.\n");

        byte[] pdf = BenchmarkPdfRenderer.RenderMarkdown(markdown.ToString(), Info(), TestContext.Current.CancellationToken);

        using var reader = PdfDocument.Open(pdf);
        Assert.True(reader.NumberOfPages >= 3, $"Expected the section to run across pages, got {reader.NumberOfPages}.");
        // The section starts on page 1, right after the first one, as a flowing section does.
        Assert.Contains("Questiondetails", Squash(reader.GetPage(1).Text));
        Assert.Contains("TALLMARKER", AllText(pdf));
    }

    private static BenchmarkReportRenderChart[] TwoCharts() => new[]
    {
        Chart("p1a-quality", 800, 450, 10, "Quality index", "Higher is better.", "Bar chart of the quality index of three models."),
        Chart("s1-quality-speed", 900, 500, 90, "Quality against speed", "Up and left is better.", "Scatter plot of quality against median answer time.")
    };

    private static BenchmarkReportRenderChart Chart(string key, int width, int height, byte shade, string title, string caption, string alt)
    {
        byte[] png = TestPngs.Make(width, height, shade);
        return new BenchmarkReportRenderChart
        {
            FigureKey = key,
            Title = title,
            Caption = caption,
            AltText = alt,
            Png = png,
            WidthPx = width,
            HeightPx = height,
            Sha256 = BenchmarkReportChartStore.Sha256Hex(png)
        };
    }

    /// <summary>The images drawn on all pages together, and the page count.</summary>
    private static (int Images, int Pages) ImageCount(byte[] pdf)
    {
        using var reader = PdfDocument.Open(pdf);
        return (reader.GetPages().Sum(p => p.GetImages().Count()), reader.NumberOfPages);
    }

    /// <summary>Every structure element's type and alternative text, walking the structure tree from its root.</summary>
    private static List<(string Type, string? Alt)> StructureElements(PdfDocument reader)
    {
        var result = new List<(string Type, string? Alt)>();
        if (!reader.Structure.Catalog.CatalogDictionary.Data.TryGetValue("StructTreeRoot", out var root)) return result;

        var seen = new HashSet<(long, int)>();
        void Visit(IToken token, int depth)
        {
            if (depth > 256) return;
            if (token is IndirectReferenceToken reference)
            {
                if (!seen.Add((reference.Data.ObjectNumber, reference.Data.Generation))) return;
                token = reader.Structure.GetObject(reference.Data).Data;
            }

            switch (token)
            {
                case ArrayToken array:
                    foreach (var item in array.Data) Visit(item, depth + 1);
                    break;
                case DictionaryToken dictionary:
                    if (dictionary.Data.TryGetValue("S", out var type) && type is NameToken name)
                    {
                        string? alt = dictionary.Data.TryGetValue("Alt", out var altToken) ? TextOf(altToken) : null;
                        result.Add((name.Data, alt));
                    }
                    if (dictionary.Data.TryGetValue("K", out var kids)) Visit(kids, depth + 1);
                    break;
            }
        }

        string? TextOf(IToken token)
        {
            if (token is IndirectReferenceToken reference) token = reader.Structure.GetObject(reference.Data).Data;
            return token switch
            {
                StringToken s => s.Data,
                HexToken h => h.Data,
                _ => null
            };
        }

        Visit(root, 0);
        return result;
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
            "run-12_vs-run-14-run-13_gpt-5.6-luna-on-the-overseer-gnollhack-assistant-benchmark_Researcher_Report_detailed_anonymized.pdf",
            BenchmarkPdfFileNames.ForReportDocument(document, new BenchmarkReportRenderOptions
            {
                Disclosure = BenchmarkReportDisclosure.Detailed,
                PeerNaming = BenchmarkReportPeerNaming.Anonymized
            }));
        Assert.Equal(
            "run-12_vs-run-14-run-13_gpt-5.6-luna-on-the-overseer-gnollhack-assistant-benchmark_Researcher_Report_full_named_INTERNAL.pdf",
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
            "vs-run-14-run-13_gpt-5.6-luna-on-the-overseer-gnollhack-assistant-benchmark-executive-summary_summary_named.pdf",
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
            "run-12_vs-run-14-run-13_gpt-5.6-luna-on-the-overseer-gnollhack-assistant-benchmark_Researcher_Report_summary_named.pdf",
            BenchmarkPdfFileNames.ForReportDocument(document, options));
        Assert.Equal(
            "GPT-5.6 Luna on the Overseer GnollHack Assistant Benchmark — Report for AI Researchers and Developers",
            BenchmarkPdfDocumentInfo.ForReportDocument(document, options, BenchmarkPdfPaper.A4).Title);

        // The other audiences keep the title-derived name.
        var executive = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.ExecutiveSummary);
        Assert.Equal(
            "run-12_vs-run-14-run-13_gpt-5.6-luna-on-the-overseer-gnollhack-assistant-benchmark-executive-summary_summary_named.pdf",
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
