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
    public void AWideTable_KeepsEveryHeaderWordWhole_AndShrinksOnlyItsTextColumns()
    {
        // The minimums (89 estimated characters) exceed the A4 text width (about 80), so the text
        // columns shrink and the five numeric columns keep their minimums.
        const string wide =
            "| Question | Topic | Assessed band | Mean score | Critical errors | Refuted answer sentences | Tool calls | Model time |\n"
            + "|---|---|---|---|---|---|---|---|\n"
            + "| S1-Q1 | Throwing gems at unicorns, an item-identification-question | Intermediate | 90 | 0 | 0 | 2 | 8.1 s |\n"
            + "| S1-Q2 | Reading the map | Simple | 72 | 1 | 1 | 3 | 11.0 s |\n";
        string[] headers = { "Question", "Topic", "Assessed band", "Mean score", "Critical errors", "Refuted answer sentences", "Tool calls", "Model time" };

        var (aligns, widths, constant) = Layout(wide);

        Assert.False(constant);
        Assert.Equal(
            new[] { false, false, false, true, true, true, true, true },
            aligns.Select(a => a == BenchmarkPdfMarkdownComposer.CellAlign.Right).ToArray());

        // A header word in points as the composer estimates it: semibold characters of 5.2 points,
        // one character of slack, and the cell padding.
        static double HeaderWordPoints(string header)
            => (Math.Ceiling(header.Split(' ').Max(w => w.Length) * 1.08) + 1) * 5.2 + 8;
        for (int c = 0; c < headers.Length; c++)
        {
            Assert.True(widths[c] >= HeaderWordPoints(headers[c]) - 0.001,
                $"Column '{headers[c]}' is {widths[c]} points, narrower than its longest header word ({HeaderWordPoints(headers[c])}).");
        }

        // The numeric columns sit at their header words; the Topic column gave up the most.
        for (int c = 3; c < headers.Length; c++)
        {
            Assert.Equal(HeaderWordPoints(headers[c]), widths[c], 3);
        }
        Assert.True(widths[1] < (24 * 5.2 + 8), "The Topic column did not shrink.");
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
        Assert.Contains(Squash("PDF layout 5"), text);
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
        Assert.Contains("PDFlayout5", first);

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

    // --- The last section ----------------------------------------------------------------------------

    [Fact]
    public void TheLayoutVersion_IsFive()
    {
        Assert.Equal(5, BenchmarkPdfRenderer.LayoutVersion);
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
