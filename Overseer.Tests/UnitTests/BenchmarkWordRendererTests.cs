namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Overseer.Services.Benchmarking.Pdf;
using Overseer.Services.Benchmarking.Word;
using Xunit;
using Cp = DocumentFormat.OpenXml.CustomProperties;
using V = DocumentFormat.OpenXml.Vml;
using W = DocumentFormat.OpenXml.Wordprocessing;

/// <summary>
/// The benchmark Word documents read back with the Open XML SDK: schema validity, styles, tables,
/// lists, links, properties, the section and its frame, the watermark, the table of contents, raw
/// HTML, the size guard, the embedded fonts; and the Word endpoints of
/// <see cref="AdminBenchmarkController"/> and <see cref="AdminBenchmarkReportDocumentsController"/>.
/// </summary>
public class BenchmarkWordRendererTests
{
    private static readonly DateTime CreatedAt = new(2026, 9, 28, 10, 42, 0, DateTimeKind.Utc);

    private const string Title = "Word Fixture Title";

    /// <summary>Every block and inline the writer maps, plus nested lists, a short table row, raw HTML and an unsafe link.</summary>
    private const string RichMarkdown =
        "# Word Fixture Title\n\n" +
        "Intro with **bold**, *italic*, ~~strike~~, ++under++, ==marked== and `inline code`.\n" +
        "A soft break continues here  \nand a hard break came before this line.\n\n" +
        "# Top level heading\n\n" +
        "## Section One\n\n" +
        "### Third level\n\n" +
        "#### Fourth level\n\n" +
        "##### Fifth level\n\n" +
        "###### Sixth level\n\n" +
        "A [safe link](https://example.com/page), an autolink <https://example.org>, a mail <team@example.com>, " +
        "an ![alt text](https://example.com/i.png) image and a [bad link](javascript:alert(1)).\n\n" +
        "```csharp\nif (ready)\n\treturn;\n    indented\n```\n\n" +
        "    indented code block\n\n" +
        "- Bullet one\n" +
        "  - Nested bullet\n" +
        "- Bullet two\n\n" +
        "  A later paragraph in bullet two.\n\n" +
        "3. Third\n" +
        "4. Fourth\n" +
        "   1. Nested ordered\n\n" +
        "1) Paren first\n" +
        "2) Paren second\n\n" +
        "> A quoted paragraph.\n\n" +
        "| Name | Score | Note |\n" +
        "|------|------:|:----:|\n" +
        "| alpha | 12 | ok |\n" +
        "| beta | 7.5 | fine |\n" +
        "| gamma |\n" +
        "| delta | 3 | late |\n\n" +
        "| Metric | Value |\n" +
        "|---|---|\n" +
        "| Latency | 1,234 ms |\n" +
        "| Cost | $0.42 |\n\n" +
        "---\n\n" +
        "<div>raw html block</div>\n\n" +
        "Inline <b>tag</b> and <script>alert(1)</script> here.\n\n" +
        "## Section Two\n\nText.\n\n" +
        "## Section Three\n\nText.\n\n" +
        "## Section Four\n\nText.\n";

    private const string PlainText = "Overseer build 1.1.2\n\tTool calls: 12\n\u001B[31mcolored\u001B[0m\n\n  indented line\n";

    // --- Schema validity ---------------------------------------------------------------------------

    [Theory]
    [InlineData(BenchmarkPdfClassification.Internal, BenchmarkPdfPaper.A4, false)]
    [InlineData(BenchmarkPdfClassification.Internal, BenchmarkPdfPaper.Letter, false)]
    [InlineData(BenchmarkPdfClassification.ProviderConfidential, BenchmarkPdfPaper.A4, false)]
    [InlineData(BenchmarkPdfClassification.ProviderConfidential, BenchmarkPdfPaper.Letter, false)]
    [InlineData(BenchmarkPdfClassification.Internal, BenchmarkPdfPaper.A4, true)]
    [InlineData(BenchmarkPdfClassification.Internal, BenchmarkPdfPaper.Letter, true)]
    [InlineData(BenchmarkPdfClassification.ProviderConfidential, BenchmarkPdfPaper.A4, true)]
    [InlineData(BenchmarkPdfClassification.ProviderConfidential, BenchmarkPdfPaper.Letter, true)]
    public void TheDocument_IsSchemaValid(BenchmarkPdfClassification classification, BenchmarkPdfPaper paper, bool plainText)
    {
        var info = Info(classification, paper);
        byte[] docx = plainText
            ? BenchmarkWordRenderer.RenderPlainText(PlainText, info, TestContext.Current.CancellationToken)
            : BenchmarkWordRenderer.RenderMarkdown(RichMarkdown, info, TestContext.Current.CancellationToken);

        AssertValid(docx);
    }

    // --- Styles ------------------------------------------------------------------------------------

    [Fact]
    public void Headings_UseWordsBuiltInStyles_AndRunsCarryOnlyMarkdownFormatting()
    {
        using var package = Open(BenchmarkWordRenderer.RenderMarkdown(RichMarkdown, Info()));
        var main = package.MainDocumentPart!;
        var body = main.Document!.Body!;

        var styles = main.StyleDefinitionsPart!.Styles!.Elements<W.Style>().ToDictionary(s => s.StyleId!.Value!);
        for (int level = 1; level <= 6; level++)
        {
            string id = "Heading" + level.ToString(CultureInfo.InvariantCulture);
            Assert.Equal("heading " + level.ToString(CultureInfo.InvariantCulture), styles[id].StyleName!.Val!.Value);
            Assert.Contains(body.Descendants<W.Paragraph>(), p => StyleOf(p) == id);
        }
        Assert.Equal("Normal", styles["Normal"].StyleName!.Val!.Value);
        Assert.True(styles["Normal"].Default!.Value);
        Assert.Equal("Title", styles["Title"].StyleName!.Val!.Value);
        Assert.Equal("Hyperlink", styles["Hyperlink"].StyleName!.Val!.Value);
        Assert.Equal("Table Grid", styles["TableGrid"].StyleName!.Val!.Value);

        var title = Assert.Single(body.Descendants<W.Paragraph>(), p => StyleOf(p) == "Title");
        Assert.Equal(Title, TextOf(title));
        Assert.DoesNotContain(body.Descendants<W.Paragraph>(), p => StyleOf(p) == "Heading1" && TextOf(p) == Title);

        var allowed = new[] { typeof(W.RunStyle), typeof(W.Bold), typeof(W.Italic), typeof(W.Strike), typeof(W.Underline), typeof(W.Shading) };
        Assert.All(body.Descendants<W.RunProperties>().SelectMany(r => r.ChildElements), e => Assert.Contains(e.GetType(), allowed));
        Assert.Empty(body.Descendants<W.ParagraphMarkRunProperties>());

        var intro = body.Descendants<W.Paragraph>().First(p => TextOf(p).StartsWith("Intro with", StringComparison.Ordinal));
        Assert.Contains(intro.Descendants<W.Run>(), r => r.RunProperties?.Bold != null && TextOf(r) == "bold");
        Assert.Contains(intro.Descendants<W.Run>(), r => r.RunProperties?.Italic != null && TextOf(r) == "italic");
        Assert.Contains(intro.Descendants<W.Run>(), r => r.RunProperties?.Strike != null && TextOf(r) == "strike");
        Assert.Contains(intro.Descendants<W.Run>(), r => r.RunProperties?.RunStyle?.Val?.Value == "InlineCode" && TextOf(r) == "inline code");
        Assert.NotEmpty(intro.Descendants<W.Break>());

        var quote = body.Descendants<W.Paragraph>().Single(p => TextOf(p) == "A quoted paragraph.");
        Assert.Equal("Quote", StyleOf(quote));
        Assert.Contains(body.Descendants<W.Paragraph>(), p => StyleOf(p) == "HorizontalRule");
    }

    [Fact]
    public void CodeBlocks_AreOneCodeBlockParagraphPerLine_WithLeadingSpacesKept()
    {
        using var package = Open(BenchmarkWordRenderer.RenderMarkdown(RichMarkdown, Info()));
        var code = package.MainDocumentPart!.Document!.Body!.Descendants<W.Paragraph>().Where(p => StyleOf(p) == "CodeBlock").ToList();

        Assert.Contains(code, p => TextOf(p) == "if (ready)");
        var tabbed = Assert.Single(code, p => TextOf(p) == "    return;");
        Assert.Equal(SpaceProcessingModeValues.Preserve, tabbed.Descendants<W.Text>().Single().Space!.Value);
        Assert.Contains(code, p => TextOf(p) == "    indented");
        Assert.Contains(code, p => TextOf(p) == "indented code block");
    }

    [Fact]
    public void PlainText_IsOneCodeBlockParagraphPerLine_AfterTheTitleBlock()
    {
        using var package = Open(BenchmarkWordRenderer.RenderPlainText(PlainText, Info(BenchmarkPdfClassification.Internal)));
        var body = package.MainDocumentPart!.Document!.Body!;
        var code = body.Elements<W.Paragraph>().Where(p => StyleOf(p) == "CodeBlock").ToList();

        Assert.Equal(6, code.Count);
        Assert.Equal("Overseer build 1.1.2", TextOf(code[0]));
        Assert.Equal("    Tool calls: 12", TextOf(code[1]));
        Assert.Contains("colored", TextOf(code[2]), StringComparison.Ordinal);
        Assert.Equal("  indented line", TextOf(code[4]));
        Assert.True(body.Elements<W.Paragraph>().ToList().IndexOf(code[0])
            > body.Elements<W.Paragraph>().ToList().FindIndex(p => StyleOf(p) == "ClassificationInternal"));
    }

    // --- Tables ------------------------------------------------------------------------------------

    [Fact]
    public void Tables_RepeatTheirHeaderRows_KeepBodyRowsWhole_AndRightAlignANumericColumn()
    {
        using var package = Open(BenchmarkWordRenderer.RenderMarkdown(RichMarkdown, Info()));
        var body = package.MainDocumentPart!.Document!.Body!;
        var tables = body.Elements<W.Table>().Where(t => t.GetFirstChild<W.TableProperties>()?.TableStyle?.Val?.Value == "GnollBenchTable").ToList();
        Assert.Equal(2, tables.Count);

        foreach (var table in tables)
        {
            var rows = table.Elements<W.TableRow>().ToList();
            Assert.NotNull(rows[0].TableRowProperties?.GetFirstChild<W.TableHeader>());
            Assert.All(rows.Skip(1), r => Assert.NotNull(r.TableRowProperties?.GetFirstChild<W.CantSplit>()));
            int columns = table.GetFirstChild<W.TableGrid>()!.Elements<W.GridColumn>().Count();
            Assert.All(rows, r => Assert.Equal(columns, r.Elements<W.TableCell>().Sum(c => c.TableCellProperties?.GridSpan?.Val?.Value ?? 1)));
            Assert.All(table.Descendants<W.TableCell>(), c => Assert.IsType<W.Paragraph>(c.LastChild));
        }

        // The two tables are kept apart, or Word would merge them.
        Assert.IsType<W.Paragraph>(tables[0].NextSibling());

        var metrics = tables.Single(t => TextOf(t).StartsWith("MetricValue", StringComparison.Ordinal));
        var metricRows = metrics.Elements<W.TableRow>().ToList();
        Assert.All(metricRows, r => Assert.Equal(W.JustificationValues.Right, Justification(r.Elements<W.TableCell>().ElementAt(1))));
        Assert.All(metricRows, r => Assert.Null(Justification(r.Elements<W.TableCell>().ElementAt(0))));

        var scores = tables.Single(t => TextOf(t).StartsWith("NameScoreNote", StringComparison.Ordinal));
        var shortRow = scores.Elements<W.TableRow>().Single(r => TextOf(r) == "gamma");
        Assert.Equal(3, shortRow.Elements<W.TableCell>().Count());
        Assert.All(scores.Elements<W.TableRow>(), r => Assert.Equal(W.JustificationValues.Center, Justification(r.Elements<W.TableCell>().ElementAt(2))));
    }

    // --- Lists -------------------------------------------------------------------------------------

    [Fact]
    public void OrderedLists_RestartAtTheirOwnStart_AndNestedItemsSitOneLevelDown()
    {
        using var package = Open(BenchmarkWordRenderer.RenderMarkdown(RichMarkdown, Info()));
        var main = package.MainDocumentPart!;
        var paragraphs = main.Document!.Body!.Descendants<W.Paragraph>().ToList();
        var numbering = main.NumberingDefinitionsPart!.Numbering!;

        int third = NumberingId(paragraphs.Single(p => TextOf(p) == "Third"));
        int paren = NumberingId(paragraphs.Single(p => TextOf(p) == "Paren first"));
        int nestedOrdered = NumberingId(paragraphs.Single(p => TextOf(p) == "Nested ordered"));
        Assert.NotEqual(third, paren);
        Assert.NotEqual(third, nestedOrdered);
        Assert.Equal(third, NumberingId(paragraphs.Single(p => TextOf(p) == "Fourth")));

        var instance = numbering.Elements<W.NumberingInstance>().Single(n => n.NumberID!.Value == third);
        Assert.Equal(3, instance.Elements<W.LevelOverride>().Single().StartOverrideNumberingValue!.Val!.Value);

        Assert.Equal(1, Level(paragraphs.Single(p => TextOf(p) == "Nested bullet")));
        Assert.Equal(1, Level(paragraphs.Single(p => TextOf(p) == "Nested ordered")));
        Assert.Equal(0, Level(paragraphs.Single(p => TextOf(p) == "Bullet one")));

        var later = paragraphs.Single(p => TextOf(p) == "A later paragraph in bullet two.");
        Assert.Equal("ListParagraph", StyleOf(later));
        Assert.Null(later.ParagraphProperties!.NumberingProperties);
        Assert.NotNull(later.ParagraphProperties.Indentation?.Left);

        var parenAbstract = numbering.Elements<W.NumberingInstance>().Single(n => n.NumberID!.Value == paren).AbstractNumId!.Val!.Value;
        var parenLevel = numbering.Elements<W.AbstractNum>().Single(a => a.AbstractNumberId!.Value == parenAbstract)
            .Elements<W.Level>().Single(l => l.LevelIndex!.Value == 0);
        Assert.Equal("%1)", parenLevel.LevelText!.Val!.Value);
    }

    // --- Links -------------------------------------------------------------------------------------

    [Fact]
    public void ASafeLink_IsAnExternalHyperlink_AndAJavascriptLinkIsPlainText()
    {
        using var package = Open(BenchmarkWordRenderer.RenderMarkdown(RichMarkdown, Info()));
        var main = package.MainDocumentPart!;
        var body = main.Document!.Body!;

        var safe = body.Descendants<W.Hyperlink>().Single(h => TextOf(h) == "safe link");
        var relationship = main.HyperlinkRelationships.Single(r => r.Id == safe.Id!.Value);
        Assert.True(relationship.IsExternal);
        Assert.Equal(new Uri("https://example.com/page"), relationship.Uri);
        Assert.Equal("Hyperlink", safe.Descendants<W.RunStyle>().Single().Val!.Value);

        var mail = body.Descendants<W.Hyperlink>().Single(h => TextOf(h) == "team@example.com");
        Assert.Equal("mailto", main.HyperlinkRelationships.Single(r => r.Id == mail.Id!.Value).Uri.Scheme);

        Assert.Contains(body.Descendants<W.Run>(), r => TextOf(r) == "bad link" && r.Ancestors<W.Hyperlink>().Count() == 0);
        Assert.DoesNotContain(main.HyperlinkRelationships, r => r.Uri.ToString().StartsWith("javascript", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("[alt text]", TextOf(body), StringComparison.Ordinal);
    }

    // --- Properties --------------------------------------------------------------------------------

    [Fact]
    public void TheProperties_CarryTheMetadata_AndTheStoredCreationDate()
    {
        var info = Info(BenchmarkPdfClassification.Internal);
        byte[] docx = BenchmarkWordRenderer.RenderMarkdown(RichMarkdown, info);
        using var package = Open(docx);

        var core = Core(package);
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        XNamespace cp = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
        XNamespace dcterms = "http://purl.org/dc/terms/";
        Assert.Equal(Title, core.Root!.Element(dc + "title")!.Value);
        Assert.Equal(BenchmarkWordRenderer.Author, core.Root.Element(dc + "creator")!.Value);
        Assert.Equal(BenchmarkWordRenderer.Author, core.Root.Element(cp + "lastModifiedBy")!.Value);
        Assert.Equal(info.SubjectLine, core.Root.Element(dc + "subject")!.Value);
        Assert.Equal("en-US", core.Root.Element(dc + "language")!.Value);
        string keywords = core.Root.Element(cp + "keywords")!.Value;
        Assert.Contains("Fixture Model", keywords, StringComparison.Ordinal);
        Assert.Contains("GnollBench", keywords, StringComparison.Ordinal);
        Assert.Equal(CreatedAt, ParseUtc(core.Root.Element(dcterms + "created")!.Value));
        Assert.Equal(CreatedAt, ParseUtc(core.Root.Element(dcterms + "modified")!.Value));

        Assert.Equal("Overseer " + BenchmarkPdfRenderer.OverseerVersion, package.ExtendedFilePropertiesPart!.Properties!.Application!.Text);

        var custom = package.CustomFilePropertiesPart!.Properties!.Elements<Cp.CustomDocumentProperty>()
            .ToDictionary(p => p.Name!.Value!, p => p.VTLPWSTR!.Text);
        Assert.Equal("INTERNAL", custom[BenchmarkWordRenderer.ClassificationProperty]);
        Assert.Equal(BenchmarkPdfRenderer.Sha256(RichMarkdown), custom[BenchmarkWordRenderer.SourceHashProperty]);
    }

    // --- Section and frame -------------------------------------------------------------------------

    [Theory]
    [InlineData(BenchmarkPdfPaper.A4, 11906, 16838)]
    [InlineData(BenchmarkPdfPaper.Letter, 12240, 15840)]
    public void TheSection_HasThePaperAndMargins_AFirstPageHeader_AndPageFieldsInTheFooters(BenchmarkPdfPaper paper, int width, int height)
    {
        using var package = Open(BenchmarkWordRenderer.RenderMarkdown("Text.\n", Info(paper: paper)));
        var main = package.MainDocumentPart!;
        var section = main.Document!.Body!.Elements<W.SectionProperties>().Single();

        var size = section.GetFirstChild<W.PageSize>()!;
        Assert.Equal((uint)width, size.Width!.Value);
        Assert.Equal((uint)height, size.Height!.Value);
        var margin = section.GetFirstChild<W.PageMargin>()!;
        Assert.Equal(1134U, margin.Left!.Value);
        Assert.Equal(1134U, margin.Right!.Value);
        Assert.Equal(1020, margin.Top!.Value);
        Assert.Equal(1020, margin.Bottom!.Value);
        Assert.Equal(425U, margin.Header!.Value);
        Assert.Equal(425U, margin.Footer!.Value);
        Assert.NotNull(section.GetFirstChild<W.TitlePage>());

        Assert.Equal(2, section.Elements<W.HeaderReference>().Count());
        Assert.Equal(2, section.Elements<W.FooterReference>().Count());
        Assert.Equal(2, main.FooterParts.Count());
        Assert.All(main.FooterParts, footer =>
        {
            var instructions = footer.Footer!.Descendants<W.SimpleField>().Select(f => f.Instruction!.Value!.Trim()).ToList();
            Assert.Contains("PAGE", instructions);
            Assert.Contains("NUMPAGES", instructions);
            Assert.Contains("Word layout 1", TextOf(footer.Footer), StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData(BenchmarkPdfClassification.Internal, true)]
    [InlineData(BenchmarkPdfClassification.ProviderConfidential, false)]
    public void TheWatermark_IsWordsOwn_AndOnlyOnInternalDocuments(BenchmarkPdfClassification classification, bool expected)
    {
        using var package = Open(BenchmarkWordRenderer.RenderMarkdown("Text.\n", Info(classification)));
        var headers = package.MainDocumentPart!.HeaderParts.ToList();
        Assert.Equal(2, headers.Count);

        foreach (var header in headers)
        {
            var shapes = header.Header!.Descendants<V.Shape>()
                .Where(s => s.Id?.Value?.StartsWith(BenchmarkWordRenderer.WatermarkShapePrefix, StringComparison.Ordinal) == true)
                .ToList();
            if (expected)
            {
                var shape = Assert.Single(shapes);
                Assert.Equal("INTERNAL", shape.GetFirstChild<V.TextPath>()!.String!.Value);
                Assert.Equal("#_x0000_t136", shape.Type!.Value);
            }
            else
            {
                Assert.Empty(shapes);
            }
        }
    }

    // --- Table of contents -------------------------------------------------------------------------

    [Theory]
    [InlineData(4, true, true)]
    [InlineData(3, true, false)]
    [InlineData(4, false, false)]
    public void ATableOfContents_ListsTheSections_ExactlyWhenAllowedWithFourOfThem(int sections, bool allowed, bool expected)
    {
        string markdown = string.Concat(Enumerable.Range(1, sections).Select(i => $"## Section {i}\n\nText {i}.\n\n"));
        using var package = Open(BenchmarkWordRenderer.RenderMarkdown(markdown, Info() with { AllowTableOfContents = allowed }));
        var body = package.MainDocumentPart!.Document!.Body!;

        var contents = body.Elements<W.SdtBlock>().ToList();
        if (!expected)
        {
            Assert.Empty(contents);
            Assert.DoesNotContain(body.Descendants<W.FieldCode>(), f => f.Text.Contains("TOC", StringComparison.Ordinal));
            return;
        }

        var toc = Assert.Single(contents);
        Assert.Equal("Table of Contents", toc.Descendants<W.DocPartGallery>().Single().Val!.Value);
        Assert.Contains("TOC \\o \"2-2\" \\h \\z \\u", toc.Descendants<W.FieldCode>().Single().Text, StringComparison.Ordinal);
        Assert.True(toc.Descendants<W.FieldChar>().First().Dirty!.Value);

        var anchors = toc.Descendants<W.Hyperlink>().Select(h => h.Anchor!.Value!).ToList();
        var bookmarks = body.Descendants<W.Paragraph>()
            .Where(p => StyleOf(p) == "Heading2")
            .Select(p => p.Descendants<W.BookmarkStart>().Single().Name!.Value!)
            .ToList();
        Assert.Equal(sections, anchors.Count);
        Assert.Equal(bookmarks, anchors);
        Assert.Equal(
            Enumerable.Range(1, sections).Select(i => $"Section {i}"),
            toc.Descendants<W.Hyperlink>().Select(h => TextOf(h)));
    }

    // --- Raw HTML ----------------------------------------------------------------------------------

    [Fact]
    public void RawHtml_IsLiteralText_AndNothingIsImported()
    {
        using var package = Open(BenchmarkWordRenderer.RenderMarkdown(RichMarkdown, Info()));
        var main = package.MainDocumentPart!;
        string text = TextOf(main.Document!.Body!);

        Assert.Contains("<div>raw html block</div>", text, StringComparison.Ordinal);
        Assert.Contains("<b>tag</b>", text, StringComparison.Ordinal);
        Assert.Contains("<script>alert(1)</script>", text, StringComparison.Ordinal);
        Assert.Empty(main.AlternativeFormatImportParts);
        Assert.Empty(main.Document.Descendants<W.AltChunk>());
    }

    // --- Guard and cancellation --------------------------------------------------------------------

    [Fact]
    public void ASourceOverTheGuard_IsRefusedBeforeRendering()
    {
        string huge = new('a', BenchmarkWordRenderer.MaxSourceCharacters + 1);

        var markdown = Assert.Throws<BenchmarkWordSourceTooLargeException>(() => BenchmarkWordRenderer.RenderMarkdown(huge, Info()));
        var plain = Assert.Throws<BenchmarkWordSourceTooLargeException>(() => BenchmarkWordRenderer.RenderPlainText(huge, Info()));

        Assert.Equal(BenchmarkWordRenderer.MaxSourceCharacters + 1, markdown.Characters);
        Assert.Equal(BenchmarkWordRenderer.MaxSourceCharacters + 1, plain.Characters);
        Assert.Contains("Word document", markdown.Message, StringComparison.Ordinal);
        Assert.True(BenchmarkWordRenderer.IsTooLarge(huge));
        Assert.False(BenchmarkWordRenderer.IsTooLarge(huge[..BenchmarkWordRenderer.MaxSourceCharacters]));
    }

    [Fact]
    public void ACanceledRender_Stops()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => BenchmarkWordRenderer.RenderMarkdown("# A\n\nText.\n", Info(), cts.Token));
        Assert.ThrowsAny<OperationCanceledException>(() => BenchmarkWordRenderer.RenderPlainText("Text.", Info(), cts.Token));
    }

    // --- Fonts -------------------------------------------------------------------------------------

    [Fact]
    public void SixFaces_AreEmbeddedObfuscated_AndDeobfuscateToTheOriginalFonts()
    {
        using var package = Open(BenchmarkWordRenderer.RenderMarkdown("Text.\n", Info()));
        var main = package.MainDocumentPart!;
        Assert.NotNull(main.DocumentSettingsPart!.Settings!.EmbedTrueTypeFonts);
        Assert.Null(main.DocumentSettingsPart.Settings.SaveSubsetFonts);

        var fontTable = main.FontTablePart!;
        var fonts = fontTable.Fonts!.Elements<W.Font>().ToDictionary(f => f.Name!.Value!);
        Assert.Equal(new[] { "Source Code Pro", "Source Sans 3" }, fonts.Keys.OrderBy(k => k, StringComparer.Ordinal));

        var embeds = fonts.Values.SelectMany(f => f.Elements<W.FontRelationshipType>()).ToList();
        Assert.Equal(6, embeds.Count);
        Assert.NotNull(fonts["Source Sans 3"].EmbedRegularFont);
        Assert.NotNull(fonts["Source Sans 3"].EmbedItalicFont);
        Assert.NotNull(fonts["Source Sans 3"].EmbedBoldFont);
        Assert.NotNull(fonts["Source Sans 3"].EmbedBoldItalicFont);
        Assert.NotNull(fonts["Source Code Pro"].EmbedRegularFont);
        Assert.NotNull(fonts["Source Code Pro"].EmbedBoldFont);

        var originals = new[]
        {
            "SourceSans3-Regular.ttf", "SourceSans3-It.ttf", "SourceSans3-Bold.ttf", "SourceSans3-BoldIt.ttf",
            "SourceCodePro-Regular.ttf", "SourceCodePro-Bold.ttf"
        }.Select(BenchmarkPdfResources.FontFile).ToList();

        var matched = new HashSet<int>();
        foreach (var embed in embeds)
        {
            var part = Assert.IsType<FontPart>(fontTable.GetPartById(embed.Id!.Value!));
            Assert.Equal("application/vnd.openxmlformats-officedocument.obfuscatedFont", part.ContentType);
            byte[] stored = ReadAll(part);

            byte[] restored = Deobfuscate(stored, embed.FontKey!.Value!);
            int index = originals.FindIndex(o => o.AsSpan().SequenceEqual(restored.AsSpan()));
            Assert.True(index >= 0, $"{embed.LocalName} does not de-obfuscate to an embedded font.");
            Assert.False(stored.AsSpan(0, 32).SequenceEqual(originals[index].AsSpan(0, 32)), "The font's header is not obfuscated.");
            matched.Add(index);
        }
        Assert.Equal(6, matched.Count);
    }

    // --- File names --------------------------------------------------------------------------------

    [Fact]
    public void WordNames_AreThePdfNamesWithDocx()
    {
        var document = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.TechnicalReport);

        Assert.Equal(
            "gpt-5.6-luna-on-the-overseer-gnollhack-assistant-benchmark-technical-report_detailed_anonymized.docx",
            BenchmarkPdfFileNames.ForReportDocument(document, new BenchmarkReportRenderOptions
            {
                Disclosure = BenchmarkReportDisclosure.Detailed,
                PeerNaming = BenchmarkReportPeerNaming.Anonymized
            }, "docx"));
        Assert.Equal("Suite_Model_20260928_104200_INTERNAL.docx", BenchmarkPdfFileNames.InternalFileName("Suite_Model_20260928_104200.md", "docx"));
        Assert.Equal("Suite_Model_20260928_104200_INTERNAL.pdf", BenchmarkPdfFileNames.InternalPdfName("Suite_Model_20260928_104200.md"));
    }

    // --- Run file endpoints ------------------------------------------------------------------------

    [Fact]
    public async Task RunWordEndpoints_AreNotFoundForAnUnknownRun_AndRefuseAnUnknownPaper()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = BenchmarkRunExamTests.InMemoryOptions();
        await using var db = new ApplicationDbContext(options);
        var controller = RunController(db);
        var body = new BenchmarkRunDiagnosticsPdfRequest { Text = "Diagnostics", CapturedAtUtc = "2026-09-28T10:42:00Z" };

        Assert.IsType<NotFoundResult>(await controller.GetRunReportDocx(9999, null, ct));
        Assert.IsType<NotFoundResult>(await controller.GetRunToolCallLogDocx(9999, "a4", ct));
        Assert.IsType<NotFoundResult>(await controller.RenderRunDiagnosticsDocx(9999, "letter", body, ct));

        Assert.IsType<BadRequestObjectResult>(await controller.GetRunReportDocx(9999, "a3", ct));
        Assert.IsType<BadRequestObjectResult>(await controller.GetRunToolCallLogDocx(9999, "legal", ct));
        Assert.IsType<BadRequestObjectResult>(await controller.RenderRunDiagnosticsDocx(9999, "tabloid", body, ct));
    }

    [Fact]
    public async Task DiagnosticsDocx_RefusesEmptyText_AndAnUnreadableCaptureTime()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = BenchmarkRunExamTests.InMemoryOptions();
        var seeded = await BenchmarkRunExamTests.SeedSuiteWithRunsAsync(options);
        await using var db = new ApplicationDbContext(options);
        var controller = RunController(db);
        long runId = seeded.RunIds[0];

        Assert.IsType<BadRequestObjectResult>(await controller.RenderRunDiagnosticsDocx(runId, null, null, ct));
        Assert.IsType<BadRequestObjectResult>(await controller.RenderRunDiagnosticsDocx(runId, null,
            new BenchmarkRunDiagnosticsPdfRequest { Text = "  ", CapturedAtUtc = "2026-09-28T10:42:00Z" }, ct));
        Assert.IsType<BadRequestObjectResult>(await controller.RenderRunDiagnosticsDocx(runId, null,
            new BenchmarkRunDiagnosticsPdfRequest { Text = "Diagnostics", CapturedAtUtc = "yesterday" }, ct));
    }

    [Fact]
    public async Task DiagnosticsDocx_IsAnInternalWordDocument_DatedAtItsCapture()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = BenchmarkRunExamTests.InMemoryOptions();
        var seeded = await BenchmarkRunExamTests.SeedSuiteWithRunsAsync(options);
        await using var db = new ApplicationDbContext(options);
        var controller = RunController(db);
        long runId = seeded.RunIds[0];

        var file = Assert.IsType<FileContentResult>(await controller.RenderRunDiagnosticsDocx(runId, null,
            new BenchmarkRunDiagnosticsPdfRequest { Text = "Overseer build 1.1.2\nTool calls: 12", CapturedAtUtc = "2026-09-28T10:42:00Z" }, ct));

        Assert.Equal(BenchmarkWordRenderer.ContentType, file.ContentType);
        Assert.Equal($"Isolation_Suite_gpt-5.6-luna_run{runId}_diagnostics_INTERNAL.docx", file.FileDownloadName);
        AssertValid(file.FileContents);
        using var package = Open(file.FileContents);
        XNamespace dcterms = "http://purl.org/dc/terms/";
        Assert.Equal(CreatedAt, ParseUtc(Core(package).Root!.Element(dcterms + "created")!.Value));
        Assert.Contains("Tool calls: 12", TextOf(package.MainDocumentPart!.Document!.Body!), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunReportDocx_IsNamedAfterTheMarkdown_AndIsValid()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = BenchmarkRunExamTests.InMemoryOptions();
        var seeded = await BenchmarkRunExamTests.SeedSuiteWithRunsAsync(options);
        await using var db = new ApplicationDbContext(options);
        var controller = RunController(db);
        long runId = seeded.RunIds[0];
        var completed = (await db.BenchmarkRuns.AsNoTracking().SingleAsync(r => r.Id == runId, ct)).CompletedAtUtc!.Value;

        var markdown = Assert.IsType<FileContentResult>(await controller.GetRunReport(runId));
        var docx = Assert.IsType<FileContentResult>(await controller.GetRunReportDocx(runId, "letter", ct));

        Assert.Equal(BenchmarkWordRenderer.ContentType, docx.ContentType);
        Assert.EndsWith(".md", markdown.FileDownloadName, StringComparison.Ordinal);
        Assert.Equal(markdown.FileDownloadName[..^3] + "_INTERNAL.docx", docx.FileDownloadName);
        AssertValid(docx.FileContents);
        using var package = Open(docx.FileContents);
        XNamespace dcterms = "http://purl.org/dc/terms/";
        Assert.Equal(completed, ParseUtc(Core(package).Root!.Element(dcterms + "created")!.Value));
    }

    [Fact]
    public async Task ToolCallLogDocx_IsNamedAfterTheMarkdown()
    {
        var ct = TestContext.Current.CancellationToken;
        var options = BenchmarkRunExamTests.InMemoryOptions();
        var seeded = await BenchmarkRunExamTests.SeedSuiteWithRunsAsync(options);
        await using var db = new ApplicationDbContext(options);
        var controller = RunController(db);
        long runId = seeded.RunIds[0];

        var markdown = Assert.IsType<FileContentResult>(await controller.GetRunToolCallLog(runId));
        var docx = Assert.IsType<FileContentResult>(await controller.GetRunToolCallLogDocx(runId, null, ct));

        Assert.Equal(BenchmarkWordRenderer.ContentType, docx.ContentType);
        Assert.Equal(markdown.FileDownloadName[..^3] + "_INTERNAL.docx", docx.FileDownloadName);
        AssertValid(docx.FileContents);
        using var package = Open(docx.FileContents);
        Assert.Contains(BenchmarkPdfDocumentInfo.TeamOnlyStamp, TextOf(package.MainDocumentPart!.Document!.Body!), StringComparison.Ordinal);
    }

    // --- Report document endpoint ------------------------------------------------------------------

    [Fact]
    public async Task RenderDocx_AnswersAsRenderPdfDoes_AndRefusesAnUnknownPaper()
    {
        var options = BenchmarkRunExamTests.InMemoryOptions();
        await using var db = new ApplicationDbContext(options);
        var document = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.InternalBrief);
        document.Id = 0;
        db.BenchmarkReportDocuments.Add(document);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var controller = new AdminBenchmarkReportDocumentsController(
            new BenchmarkReportRenderService(db, NullLogger<BenchmarkReportRenderService>.Instance));

        Assert.IsType<NotFoundResult>(await controller.RenderDocx(document.Id + 1000, "full", "named", null, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.RenderDocx(document.Id, "summary", "named", null, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.RenderDocx(document.Id, "everything", "named", null, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.RenderDocx(document.Id, "3", "named", null, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.RenderDocx(document.Id, "full", "pseudonymous", null, CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await controller.RenderDocx(document.Id, "full", "named", "a3", CancellationToken.None));
    }

    [Fact]
    public async Task RenderDocx_ReturnsAWordDocument_NamedAsTheDownloadCenterNamesTheMarkdown()
    {
        var options = BenchmarkRunExamTests.InMemoryOptions();
        await using var db = new ApplicationDbContext(options);
        var document = BenchmarkReportPackFixture.Document(BenchmarkReportAudience.TechnicalReport);
        document.Id = 0;
        db.BenchmarkReportDocuments.Add(document);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var controller = new AdminBenchmarkReportDocumentsController(
            new BenchmarkReportRenderService(db, NullLogger<BenchmarkReportRenderService>.Instance));

        var provider = Assert.IsType<FileContentResult>(
            await controller.RenderDocx(document.Id, "detailed", "anonymized", "letter", CancellationToken.None));
        var full = Assert.IsType<FileContentResult>(
            await controller.RenderDocx(document.Id, "Full", "Named", null, CancellationToken.None));

        Assert.Equal(BenchmarkWordRenderer.ContentType, provider.ContentType);
        Assert.Equal(
            "gpt-5.6-luna-on-the-overseer-gnollhack-assistant-benchmark-technical-report_detailed_anonymized.docx",
            provider.FileDownloadName);
        Assert.Equal(
            "gpt-5.6-luna-on-the-overseer-gnollhack-assistant-benchmark-technical-report_full_named_INTERNAL.docx",
            full.FileDownloadName);
        AssertValid(provider.FileContents);
        AssertValid(full.FileContents);

        using var package = Open(provider.FileContents);
        XNamespace dcterms = "http://purl.org/dc/terms/";
        Assert.Equal(BenchmarkReportPackFixture.CreatedAt, ParseUtc(Core(package).Root!.Element(dcterms + "created")!.Value));
        Assert.Empty(package.MainDocumentPart!.HeaderParts.SelectMany(h => h.Header!.Descendants<V.Shape>()));
    }

    // --- Helpers -----------------------------------------------------------------------------------

    /// <summary>Only the DbContext is used by the run-file actions.</summary>
    private static AdminBenchmarkController RunController(ApplicationDbContext db) => new(
        db, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);

    private static BenchmarkPdfDocumentInfo Info(
        BenchmarkPdfClassification classification = BenchmarkPdfClassification.ProviderConfidential,
        BenchmarkPdfPaper paper = BenchmarkPdfPaper.A4) => new()
    {
        DocumentKind = "Technical Report",
        Title = Title,
        SubjectLine = "Fixture Suite · run #12",
        Classification = classification,
        ClassificationText = classification == BenchmarkPdfClassification.Internal
            ? BenchmarkPdfDocumentInfo.TeamOnlyStamp
            : "Confidential. Prepared for the fixture's provider.",
        Facts = new[] { new BenchmarkPdfFact("Run", "#12"), new BenchmarkPdfFact("Suite", "Fixture Suite") },
        CreatedAtUtc = CreatedAt,
        Keywords = new[] { "Fixture Model", "Fixture Suite", "GnollBench" },
        Paper = paper
    };

    private static WordprocessingDocument Open(byte[] docx) => WordprocessingDocument.Open(new MemoryStream(docx), false);

    /// <summary>No schema or semantic error against Office 2019, every error listed when there is one.</summary>
    private static void AssertValid(byte[] docx)
    {
        using var package = Open(docx);
        var errors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(package).ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine,
            errors.Select(e => $"{e.Part?.Uri} {e.Path?.XPath} [{e.Id}] {e.Description}")));
    }

    private static XDocument Core(WordprocessingDocument package)
    {
        using var stream = package.CoreFilePropertiesPart!.GetStream();
        return XDocument.Load(stream);
    }

    private static DateTime ParseUtc(string value)
        => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    private static string? StyleOf(W.Paragraph paragraph) => paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;

    private static string TextOf(OpenXmlElement element) => string.Concat(element.Descendants<W.Text>().Select(t => t.Text));

    private static W.JustificationValues? Justification(W.TableCell cell)
        => cell.Elements<W.Paragraph>().First().ParagraphProperties?.Justification?.Val?.Value;

    private static int NumberingId(W.Paragraph paragraph) => paragraph.ParagraphProperties!.NumberingProperties!.NumberingId!.Val!.Value;

    private static int Level(W.Paragraph paragraph) => paragraph.ParagraphProperties!.NumberingProperties!.NumberingLevelReference!.Val!.Value;

    private static byte[] ReadAll(OpenXmlPart part)
    {
        using var stream = part.GetStream();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    /// <summary>
    /// ECMA-376 Part 1, § 17.8.1, written independently of the renderer: the key is the font key GUID's
    /// bytes as they appear in the string, last byte first, XORed over bytes 0–15 and again over 16–31.
    /// </summary>
    private static byte[] Deobfuscate(byte[] data, string fontKey)
    {
        // Where each byte's two hex digits start in "{XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX}", last byte first.
        int[] positions = { 35, 33, 31, 29, 27, 25, 22, 20, 17, 15, 12, 10, 7, 5, 3, 1 };
        byte[] key = positions.Select(p => byte.Parse(fontKey.AsSpan(p, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToArray();

        byte[] result = (byte[])data.Clone();
        for (int i = 0; i < 16; i++)
        {
            result[i] ^= key[i];
            result[i + 16] ^= key[i];
        }
        return result;
    }
}
