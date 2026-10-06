namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Overseer.Services.Benchmarking.Pdf;
using Overseer.Services.Benchmarking.Word;
using Xunit;
using W = DocumentFormat.OpenXml.Wordprocessing;

/// <summary>
/// The Word document's tables set by the PDF's table layout: the smaller text size on every run, the
/// short headers with their legend below the table, and the Topic column on a merged second row kept
/// with its row; and a table that fits left as it was.
/// </summary>
public class BenchmarkWordTableLayoutTests
{
    [Fact]
    public void TheBatteryProfile_PrintsItsShortHeader_AtEightPoints_WithALegendBelowTheTable()
    {
        byte[] docx = BenchmarkWordRenderer.RenderMarkdown(BenchmarkPdfTableLayoutTests.BatteryProfile, BenchmarkPdfTableLayoutTests.Info(), TestContext.Current.CancellationToken);
        AssertValid(docx);

        using var package = Open(docx);
        var table = Assert.Single(Tables(package));
        var rows = table.Elements<W.TableRow>().ToList();
        Assert.Equal(3, rows.Count);
        Assert.Equal(10, table.GetFirstChild<W.TableGrid>()!.Elements<W.GridColumn>().Count());

        var headers = rows[0].Elements<W.TableCell>().Select(TextOf).ToList();
        Assert.Equal("Contrib.", headers[4]);
        Assert.Equal("Cost per run, graders included", headers[8]);
        Assert.DoesNotContain("Contribution", headers);

        Assert.All(table.Descendants<W.Run>(), run => Assert.Equal("16", run.RunProperties?.FontSize?.Val?.Value));

        var legend = Assert.IsType<W.Paragraph>(table.NextSibling());
        Assert.Equal("SourceLine", StyleOf(legend));
        Assert.Equal("Contrib.: Contribution", TextOf(legend));
    }

    [Fact]
    public void ABatteryQuestionTable_PutsItsTopicOnAMergedSecondRow_KeptWithItsRow_AndStripedWithIt()
    {
        byte[] docx = BenchmarkWordRenderer.RenderMarkdown(BenchmarkPdfTableLayoutTests.BatteryQuestionResults, BenchmarkPdfTableLayoutTests.Info(), TestContext.Current.CancellationToken);
        AssertValid(docx);

        using var package = Open(docx);
        var table = Assert.Single(Tables(package));
        int columns = table.GetFirstChild<W.TableGrid>()!.Elements<W.GridColumn>().Count();
        Assert.Equal(9, columns);
        Assert.True(table.GetFirstChild<W.TableProperties>()!.TableLook!.NoHorizontalBand!.Value);

        var rows = table.Elements<W.TableRow>().ToList();
        Assert.Equal(1 + 3 * 2, rows.Count);
        Assert.DoesNotContain("Topic", rows[0].Elements<W.TableCell>().Select(TextOf));
        Assert.All(rows, r => Assert.Equal(columns, r.Elements<W.TableCell>().Sum(c => c.TableCellProperties?.GridSpan?.Val?.Value ?? 1)));

        string[] topics = { "Runewords, their effects, and where each works", "Implementation-level differences", "modifiers" };
        for (int pair = 0; pair < 3; pair++)
        {
            var main = rows[1 + 2 * pair];
            var second = rows[2 + 2 * pair];

            var cell = Assert.Single(second.Elements<W.TableCell>());
            Assert.Equal(columns, cell.TableCellProperties!.GridSpan!.Val!.Value);
            Assert.Equal("Topic: " + topics[pair], TextOf(cell));
            Assert.NotNull(second.TableRowProperties?.GetFirstChild<W.CantSplit>());
            Assert.All(main.Descendants<W.Paragraph>(), p => Assert.NotNull(p.ParagraphProperties?.KeepNext));

            // The second pair is striped, both rows of it, and the others are not.
            string? expectedFill = pair == 1 ? BenchmarkDocumentPalette.Zebra : null;
            Assert.All(main.Elements<W.TableCell>().Append(cell), c => Assert.Equal(expectedFill, c.TableCellProperties?.Shading?.Fill?.Value));
        }

        Assert.All(table.Descendants<W.Run>(), run => Assert.Equal("18", run.RunProperties?.FontSize?.Val?.Value));
        Assert.False(table.NextSibling() is W.Paragraph next && StyleOf(next) == "SourceLine", "A table without short headers has a legend.");
    }

    [Fact]
    public void ATableThatFits_KeepsItsStyleSize_StyleBanding_AndNoLegend()
    {
        const string markdown = "| Model | Assessed band | Score |\n|---|---|---|\n| Luna | Intermediate | 84 |\n| Sol | Advanced | 79 |\n";

        byte[] docx = BenchmarkWordRenderer.RenderMarkdown(markdown, BenchmarkPdfTableLayoutTests.Info(), TestContext.Current.CancellationToken);

        using var package = Open(docx);
        var table = Assert.Single(Tables(package));
        Assert.All(table.Descendants<W.Run>(), run => Assert.Null(run.RunProperties?.FontSize));
        Assert.False(table.GetFirstChild<W.TableProperties>()!.TableLook!.NoHorizontalBand!.Value);
        Assert.Equal("Assessed band", TextOf(table.Elements<W.TableRow>().First().Elements<W.TableCell>().ElementAt(1)));
        Assert.False(table.NextSibling() is W.Paragraph next && StyleOf(next) == "SourceLine");
    }

    // --- Helpers -------------------------------------------------------------------------------------

    private static WordprocessingDocument Open(byte[] docx) => WordprocessingDocument.Open(new MemoryStream(docx), false);

    private static List<W.Table> Tables(WordprocessingDocument package)
        => package.MainDocumentPart!.Document!.Body!.Elements<W.Table>()
            .Where(t => t.GetFirstChild<W.TableProperties>()?.TableStyle?.Val?.Value == "GnollBenchTable")
            .ToList();

    /// <summary>No schema or semantic error against Office 2019, every error listed when there is one.</summary>
    private static void AssertValid(byte[] docx)
    {
        using var package = Open(docx);
        var errors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(package).ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine,
            errors.Select(e => $"{e.Part?.Uri} {e.Path?.XPath} [{e.Id}] {e.Description}")));
    }

    private static string? StyleOf(W.Paragraph paragraph) => paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;

    private static string TextOf(OpenXmlElement element) => string.Concat(element.Descendants<W.Text>().Select(t => t.Text));
}
