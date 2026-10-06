namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Overseer.Services.Benchmarking.Pdf;
using Overseer.Tests.Helpers;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using UglyToad.PdfPig;
using Xunit;
using MdTable = Markdig.Extensions.Tables.Table;
using MdTableCell = Markdig.Extensions.Tables.TableCell;
using MdTableRow = Markdig.Extensions.Tables.TableRow;

/// <summary>
/// The PDF's table layout on the report's widest tables: the text size steps, the short headers and
/// their legend, and the Topic column on a second line, each taken only when the longest words need
/// it; a figure kept with the heading above it; and the gap before an inline code span.
/// </summary>
public class BenchmarkPdfTableLayoutTests
{
    private static readonly DateTime CreatedAt = new(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>The battery profile of a researcher report, with a suite name as long as the review's.</summary>
    internal const string BatteryProfile =
        "| Suite | Weight | Intelligence Index | 95 % interval | Contribution | Scored questions | Runs | Speed Index | Cost per run, graders included | Critical-error rate |\n"
        + "|---|---|---|---|---|---|---|---|---|---|\n"
        + "| S1 · GnollHack Player Assistance Benchmark Suite | 60.0 % | 84 / 100 | 78–90 | 50.4 points | 12 of 12 questions | 2 runs | 84 / 100 | $0.081 | 0 % |\n"
        + "| S2 · GnollHack Hazards and Monsters Suite | 40.0 % | 74 / 100 | — | 29.6 points | 10 of 10 questions | 2 runs | 80 / 100 | $0.063 | 25 % |\n";

    /// <summary>A standalone report's per-question results, with the review's topics.</summary>
    internal const string PerQuestionResults =
        "| Q | Topic | Assessed band | Authored | Score | Critical error | Refuted answer sentences | Tool calls | Model time |\n"
        + "|---|---|---|---|---|---|---|---|---|\n"
        + "| Q1 | Runewords, their effects, and where each works | Intermediate | Advanced | 90 | no | 0 | 2 | 8.1 s |\n"
        + "| Q2 | Implementation-level differences | Advanced | Intermediate | 72 | no | 1 | 3 | 11.0 s |\n"
        + "| Q3 | modifiers | Simple | — | 25 | yes | 2 | 5 | 15.2 s |\n"
        + "| Q12 | Prayer timeout | Intermediate | — | 87 | no | 0 | 4 | 13.4 s |\n";

    /// <summary>A battery's per-question results, one column wider: its topics go onto a second line.</summary>
    internal const string BatteryQuestionResults =
        "| Question | Topic | Assessed band | Authored | Mean score | Runs scored | Critical errors | Refuted answer sentences | Tool calls | Model time |\n"
        + "|---|---|---|---|---|---|---|---|---|---|\n"
        + "| S1-Q1 | Runewords, their effects, and where each works | Intermediate | Advanced | 90 | 2 | 0 | 0 | 2 | 8.1 s |\n"
        + "| S1-Q12 | Implementation-level differences | Advanced | Intermediate | 72 | 2 | 1 | 1 | 3 | 11.0 s |\n"
        + "| S2-Q3 | modifiers | Simple | — | 25 | 2 | 1 | 2 | 5 | 15.2 s |\n";

    public BenchmarkPdfTableLayoutTests()
    {
        BenchmarkPdfTestSetup.Configure();
    }

    // --- The layout decision -------------------------------------------------------------------------

    [Fact]
    public void TheTextSizes_StepDownByHalfAPoint_FromTheTableSize_ToEightPoints()
    {
        Assert.Equal(new[] { 9.5f, 9f, 8.5f, 8f }, BenchmarkPdfMarkdownComposer.TableFontSizes());
    }

    [Fact]
    public void ATableThatFits_KeepsTheTableSize_AndPrintsEveryHeaderInFull()
    {
        const string markdown =
            "| Model | Assessed band | Critical errors | Contribution | Cost per run, graders included |\n"
            + "|---|---|---|---|---|\n"
            + "| Luna | Simple | 0 | 12.5 | $0.08 |\n";

        var layout = Layout(markdown);

        Assert.True(layout.Fits);
        Assert.Equal(9.5f, layout.FontSize);
        Assert.Empty(layout.Abbreviations);
        Assert.Null(layout.Legend);
        Assert.Null(layout.SecondLineColumn);
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, layout.GridColumns);
        AssertEveryColumnHoldsItsLongestWord(layout, markdown);
    }

    [Fact]
    public void TheBatteryProfile_HoldsEveryLongestWord_AtEightPoints_WithOneShortHeader()
    {
        var layout = Layout(BatteryProfile);

        Assert.True(layout.Fits);
        Assert.False(layout.Constant);
        Assert.Equal(8f, layout.FontSize);
        var abbreviation = Assert.Single(layout.Abbreviations);
        Assert.Equal(new BenchmarkTableAbbreviation(4, "Contrib.", "Contribution"), abbreviation);
        Assert.Equal("Contrib.", layout.HeaderTextOf(4));
        Assert.Null(layout.HeaderTextOf(8));
        Assert.Equal("Contrib.: Contribution", layout.Legend);
        Assert.Null(layout.SecondLineColumn);
        Assert.Equal(10, layout.GridColumns.Length);
        AssertEveryColumnHoldsItsLongestWord(layout, BatteryProfile);
    }

    [Fact]
    public void ThePerQuestionResults_HoldEveryLongestWord_AtASmallerSize_WithoutShortHeaders()
    {
        var layout = Layout(PerQuestionResults);

        Assert.True(layout.Fits);
        Assert.True(layout.FontSize < 9.5f, $"The table stayed at {layout.FontSize} pt.");
        Assert.Equal(8.5f, layout.FontSize);
        Assert.Empty(layout.Abbreviations);
        Assert.Null(layout.Legend);
        Assert.Null(layout.SecondLineColumn);
        AssertEveryColumnHoldsItsLongestWord(layout, PerQuestionResults);
    }

    [Fact]
    public void ABatteryQuestionTable_MovesItsTopicOntoASecondLine_WhenShortHeadersAreNotEnough()
    {
        var layout = Layout(BatteryQuestionResults);

        Assert.True(layout.Fits);
        Assert.Equal(1, layout.SecondLineColumn);
        Assert.Equal(new[] { 0, 2, 3, 4, 5, 6, 7, 8, 9 }, layout.GridColumns);
        Assert.Equal(9, layout.Widths.Length);
        // With the topic out of the grid, the table fits at 9 pt without a short header.
        Assert.Equal(9f, layout.FontSize);
        Assert.Empty(layout.Abbreviations);
        Assert.Null(layout.Legend);
        Assert.Equal((0, 1), layout.GridSpanOf(0, 1));
        Assert.Equal(0, layout.GridSpanOf(1, 1).Span);
        Assert.Equal((1, 1), layout.GridSpanOf(2, 1));
        Assert.Equal((0, 2), layout.GridSpanOf(0, 3));
        AssertEveryColumnHoldsItsLongestWord(layout, BatteryQuestionResults);
    }

    [Fact]
    public void ATableThatCannotFit_TakesEveryUsefulMeasure_AtTheSmallestSize()
    {
        var markdown = new StringBuilder("| Topic | Critical errors |");
        for (int c = 1; c <= 12; c++) markdown.Append(" Column").Append(c).Append(" |");
        markdown.Append('\n').Append(string.Concat(Enumerable.Repeat("|---", 14))).Append("|\n| A topic | 0 |");
        for (int c = 1; c <= 12; c++) markdown.Append(" Characteristically |");
        markdown.Append('\n');

        var layout = Layout(markdown.ToString());

        Assert.False(layout.Fits);
        Assert.Equal(BenchmarkPdfMarkdownComposer.MinTableCellSize, layout.FontSize);
        Assert.Equal(0, layout.SecondLineColumn);
        Assert.Equal("Crit.: Critical errors", layout.Legend);
    }

    [Fact]
    public void Words_BreakAtSpacesAndAfterAHyphenBeforeALetter_ButNotAtANoBreakSpace()
    {
        Assert.Equal(new[] { "Implementation-", "level", "differences" }, BenchmarkPdfMarkdownComposer.Words("Implementation-level differences"));
        Assert.Equal(new[] { "S1-", "Q12" }, BenchmarkPdfMarkdownComposer.Words("S1-Q12"));
        Assert.Equal(new[] { "-5", "12.0\u00A0%" }, BenchmarkPdfMarkdownComposer.Words("-5 12.0\u00A0%"));
        Assert.True(BenchmarkPdfMarkdownComposer.WordCharacters("illicit") < BenchmarkPdfMarkdownComposer.WordCharacters("mammoth"));
    }

    // --- The rendered tables -------------------------------------------------------------------------

    [Fact]
    public void TheReviewTables_PrintEveryLongWordWhole()
    {
        string markdown = BatteryProfile + "\n" + PerQuestionResults + "\n" + BatteryQuestionResults;

        byte[] pdf = BenchmarkPdfRenderer.RenderMarkdown(markdown, Info(), TestContext.Current.CancellationToken);

        using var reader = PdfDocument.Open(pdf);
        var words = reader.GetPages().SelectMany(p => p.GetWords()).Select(w => w.Text).ToList();
        foreach (string token in new[]
        {
            "GnollHack", "Assistance", "Benchmark", "Intelligence", "interval", "Contrib.", "questions", "included",
            "Runewords,", "Implementation-", "differences", "modifiers", "Intermediate", "sentences", "Critical"
        })
        {
            Assert.True(words.Any(w => w.Contains(token, StringComparison.Ordinal)),
                $"'{token}' is not printed whole; the words are: {string.Join(" ", words)}");
        }

        string text = string.Concat(reader.GetPages().Select(p => Squash(p.Text)));
        Assert.Contains("Contrib.:Contribution", text);
    }

    [Fact]
    public void ATopicOnASecondLine_PrintsUnderItsRow_FromTheTablesLeftEdge()
    {
        byte[] pdf = BenchmarkPdfRenderer.RenderMarkdown(BatteryQuestionResults, Info(), TestContext.Current.CancellationToken);

        using var reader = PdfDocument.Open(pdf);
        var words = reader.GetPage(1).GetWords().ToList();
        var label = words.First(w => w.Text.StartsWith("S2-Q3", StringComparison.Ordinal));
        var band = words.First(w => w.Text.StartsWith("Simple", StringComparison.Ordinal));
        var topic = words.First(w => w.Text.Contains("modifiers", StringComparison.Ordinal));
        var topicLabel = words
            .Where(w => w.Text.StartsWith("Topic:", StringComparison.Ordinal) && w.BoundingBox.Bottom < label.BoundingBox.Bottom)
            .OrderByDescending(w => w.BoundingBox.Bottom)
            .First();

        Assert.True(topic.BoundingBox.Bottom < label.BoundingBox.Bottom - 2,
            $"The topic sits at {topic.BoundingBox.Bottom}, not below its row's label at {label.BoundingBox.Bottom}.");
        Assert.True(Math.Abs(band.BoundingBox.Bottom - label.BoundingBox.Bottom) < 1, "The row's cells are not on one line.");
        Assert.True(Math.Abs(topicLabel.BoundingBox.Left - label.BoundingBox.Left) < 1.5,
            $"The second line starts at {topicLabel.BoundingBox.Left}, not at the row's left edge {label.BoundingBox.Left}.");
        Assert.True(Math.Abs(topicLabel.BoundingBox.Bottom - topic.BoundingBox.Bottom) < 1, "The label and the topic are not on one line.");
    }

    // --- A figure after a heading --------------------------------------------------------------------

    [Fact]
    public void TheBlockAHeadingKeepsWith_IsTheFirstThatPrints()
    {
        const string markdown = "## Strengths\n\n[[figure:missing-chart]]\n\n[[figure:kept-chart]]\n\nAfter the figure.\n";

        var withChart = BenchmarkPdfMarkdownComposer.Prepare(markdown, "Title", new[] { Chart("kept-chart") });
        Assert.Equal(2, BenchmarkPdfMarkdownComposer.FirstPrintedAfter(withChart.Blocks, 0, withChart));
        Assert.True(withChart.Figures.ContainsKey((Markdig.Syntax.ParagraphBlock)withChart.Blocks[2]));

        var withoutCharts = BenchmarkPdfMarkdownComposer.Prepare(markdown, "Title");
        Assert.Equal(3, BenchmarkPdfMarkdownComposer.FirstPrintedAfter(withoutCharts.Blocks, 0, withoutCharts));

        var headingOnly = BenchmarkPdfMarkdownComposer.Prepare("Text.\n\n## Last\n\n[[figure:missing-chart]]\n", "Title");
        Assert.Null(BenchmarkPdfMarkdownComposer.FirstPrintedAfter(headingOnly.Blocks, 1, headingOnly));
    }

    [Fact]
    public void AFigureDirectlyAfterAHeading_StartsOnTheHeadingsPage_WhereverThePageBreakFalls()
    {
        var charts = new[] { Chart("kept-chart") };
        bool movedPastPageOne = false;
        for (int filler = 6; filler <= 40; filler += 2)
        {
            var markdown = new StringBuilder();
            for (int i = 1; i <= filler; i++)
            {
                markdown.Append("Filler paragraph ").Append(i).Append(" moves the heading down the page.\n\n");
            }
            markdown.Append("## FIGHEADMARKER\n\n[[figure:missing-chart]]\n\n[[figure:kept-chart]]\n\nAfter the figure.\n");

            byte[] pdf = BenchmarkPdfRenderer.RenderMarkdown(markdown.ToString(), Info(), TestContext.Current.CancellationToken, charts);

            using var reader = PdfDocument.Open(pdf);
            var pages = reader.GetPages().ToList();
            int heading = pages.First(p => Squash(p.Text).Contains("FIGHEADMARKER", StringComparison.Ordinal)).Number;
            int caption = pages.First(p => Squash(p.Text).Contains("Figure1.", StringComparison.Ordinal)).Number;
            Assert.True(heading == caption, $"With {filler} filler paragraphs the heading is on page {heading} and its figure on page {caption}.");
            movedPastPageOne |= heading > 1;
        }

        Assert.True(movedPastPageOne, "No filler count pushed the heading past page 1, so no page break fell between it and its figure.");
    }

    // --- The space before inline code (review W4) ----------------------------------------------------

    /// <summary>Half the width of a space in the 10.5 pt body text: a gap below it means the space was lost.</summary>
    private const double MinimumGapPoints = 1.0;

    [Fact]
    public void AParagraph_KeepsAVisibleGapBeforeAndAfterInlineCode()
    {
        byte[] pdf = BenchmarkPdfRenderer.RenderMarkdown("Before alphaword `betacode` gammaword after.\n", Info(), TestContext.Current.CancellationToken);

        var (before, after) = GapsAroundCode(pdf);
        Assert.True(before > MinimumGapPoints, $"The gap before the code span is {before:0.00} points.");
        Assert.True(after > MinimumGapPoints, $"The gap after the code span is {after:0.00} points.");
    }

    /// <summary>
    /// QuestPDF itself, without the composer: a span ending in a space keeps that space before a
    /// monospace span, so the composer passes literal text through unchanged.
    /// </summary>
    [Fact]
    public void QuestPdf_KeepsTheSpaceAtTheEndOfASpanBeforeAMonospaceSpan()
    {
        byte[] pdf = Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(50);
            page.Content().Text(t =>
            {
                t.DefaultTextStyle(_ => BenchmarkPdfStyle.Base());
                t.Span("Before alphaword ");
                BenchmarkPdfStyle.MonoSpan(t.Span("betacode")).BackgroundColor(BenchmarkPdfStyle.InlineCodeBackground);
                t.Span(" gammaword after.");
            });
        })).GeneratePdf();

        var (before, after) = GapsAroundCode(pdf);
        Assert.True(before > MinimumGapPoints, $"The gap before the monospace span is {before:0.00} points.");
        Assert.True(after > MinimumGapPoints, $"The gap after the monospace span is {after:0.00} points.");
    }

    /// <summary>
    /// The horizontal gaps, in points, between "alphaword" and "betacode" and between "betacode" and
    /// "gammaword" on page 1: from the end of one word's last glyph to the start of the next word's first.
    /// </summary>
    private static (double Before, double After) GapsAroundCode(byte[] pdf)
    {
        using var reader = PdfDocument.Open(pdf);
        var letters = reader.GetPage(1).Letters.Where(l => !string.IsNullOrWhiteSpace(l.Value)).ToList();
        string text = string.Concat(letters.Select(l => l.Value));

        int start = text.IndexOf("alphawordbetacodegammaword", StringComparison.Ordinal);
        Assert.True(start >= 0, "The three words were not found in order: " + text);

        double Gap(int lastOfFirst) => letters[lastOfFirst + 1].StartBaseLine.X - letters[lastOfFirst].EndBaseLine.X;
        return (Gap(start + "alphaword".Length - 1), Gap(start + "alphawordbetacode".Length - 1));
    }

    // --- Helpers -------------------------------------------------------------------------------------

    /// <summary>The parts of the first table in <paramref name="markdown"/>, as the composer splits them.</summary>
    internal static (MdTable Table, List<List<MdTableCell>> Header, List<List<MdTableCell>> Body, int Columns) Parts(string markdown)
    {
        var table = BenchmarkPdfMarkdownComposer.Parse(markdown).OfType<MdTable>().First();
        var rows = table.OfType<MdTableRow>().ToList();
        var header = rows.Where(r => r.IsHeader).Select(r => r.OfType<MdTableCell>().ToList()).ToList();
        var body = rows.Where(r => !r.IsHeader).Select(r => r.OfType<MdTableCell>().ToList()).ToList();
        int columns = rows.Max(r => r.OfType<MdTableCell>().Sum(c => Math.Max(1, c.ColumnSpan)));
        return (table, header, body, columns);
    }

    internal static BenchmarkTableLayout Layout(string markdown)
    {
        var (table, header, body, columns) = Parts(markdown);
        return BenchmarkPdfMarkdownComposer.TableLayout(table, header, body, columns, markdown);
    }

    /// <summary>
    /// Every grid column at least as wide as its longest word at the layout's text size, as the composer
    /// estimates it: the word's characters by width class (semibold header words 8 % wider) at 5.2 points
    /// for 9.5 pt text, scaled to the text size, plus the cell padding. Relative widths are first spread
    /// across the A4 text width, as QuestPDF does.
    /// </summary>
    private static void AssertEveryColumnHoldsItsLongestWord(BenchmarkTableLayout layout, string markdown)
    {
        var (_, header, body, _) = Parts(markdown);
        double scale = layout.Constant ? 1 : 482 / layout.Widths.Sum(w => (double)w);
        double characterPoints = 5.2 * layout.FontSize / 9.5;

        for (int g = 0; g < layout.GridColumns.Length; g++)
        {
            int c = layout.GridColumns[g];
            var bodyWords = body
                .Select(cells => BenchmarkPdfMarkdownComposer.CellAt(cells, c))
                .Where(cell => cell != null)
                .SelectMany(cell => BenchmarkPdfMarkdownComposer.Words(BenchmarkPdfMarkdownComposer.CellText(cell!, markdown)))
                .Select(BenchmarkPdfMarkdownComposer.WordCharacters);
            string headerText = layout.HeaderTextOf(c)
                ?? BenchmarkPdfMarkdownComposer.CellText(BenchmarkPdfMarkdownComposer.CellAt(header[0], c)!, markdown);
            var headerWords = BenchmarkPdfMarkdownComposer.Words(headerText).Select(w => BenchmarkPdfMarkdownComposer.WordCharacters(w) * 1.08);

            double longest = bodyWords.Concat(headerWords).Max();
            double needed = longest * characterPoints + 8;
            double width = layout.Widths[g] * scale;
            Assert.True(width >= needed - 0.001,
                $"Column {c} ('{headerText}') is {width:0.0} points at {layout.FontSize} pt, narrower than its longest word ({needed:0.0}).");
        }
    }

    private static BenchmarkReportRenderChart Chart(string key)
    {
        byte[] png = TestPngs.Make(800, 450, 40);
        return new BenchmarkReportRenderChart
        {
            FigureKey = key,
            Title = "Kept",
            Caption = "Kept with its heading.",
            AltText = "A chart kept with its heading.",
            Png = png,
            WidthPx = 800,
            HeightPx = 450,
            Sha256 = BenchmarkReportChartStore.Sha256Hex(png)
        };
    }

    internal static BenchmarkPdfDocumentInfo Info() => new()
    {
        DocumentKind = "Technical Report",
        Title = "Table Layout Fixture",
        SubjectLine = "Fixture Suite · run #12",
        Classification = BenchmarkPdfClassification.ProviderConfidential,
        ClassificationText = "Confidential. Prepared for the fixture's provider.",
        Facts = new[] { new BenchmarkPdfFact("Run", "#12") },
        CreatedAtUtc = CreatedAt,
        Keywords = new[] { "Fixture Model", "GnollBench" }
    };

    /// <summary>The text with all whitespace removed: extraction does not reliably keep spaces.</summary>
    private static string Squash(string text) => new(text.Where(c => !char.IsWhiteSpace(c)).ToArray());
}
