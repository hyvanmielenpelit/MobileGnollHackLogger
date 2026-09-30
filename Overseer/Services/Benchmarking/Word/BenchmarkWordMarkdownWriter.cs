namespace Overseer.Services.Benchmarking.Word;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Overseer.Services.Benchmarking.Pdf;
using A = DocumentFormat.OpenXml.Drawing;
using CellAlign = Overseer.Services.Benchmarking.Pdf.BenchmarkPdfMarkdownComposer.CellAlign;
using Composer = Overseer.Services.Benchmarking.Pdf.BenchmarkPdfMarkdownComposer;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using Figure = Overseer.Services.Benchmarking.Pdf.BenchmarkPdfMarkdownComposer.Figure;
using InlineStyle = Overseer.Services.Benchmarking.Pdf.BenchmarkPdfMarkdownComposer.InlineStyle;
using MdTable = Markdig.Extensions.Tables.Table;
using MdTableCell = Markdig.Extensions.Tables.TableCell;
using MdTableRow = Markdig.Extensions.Tables.TableRow;
using Palette = Overseer.Services.Benchmarking.Pdf.BenchmarkDocumentPalette;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using WordStyles = Overseer.Services.Benchmarking.Word.BenchmarkWordStyles;

/// <summary>
/// Walks the prepared Markdig tree (<see cref="BenchmarkPdfMarkdownComposer.Prepare"/>) into
/// WordprocessingML for the body of a benchmark Word document, block for block as
/// <see cref="BenchmarkPdfMarkdownComposer"/> composes the PDF. Every block takes a named style from
/// <see cref="BenchmarkWordStyles"/>; runs carry direct formatting only for what the Markdown itself
/// marks (bold, italic, strike, underline, mark), and paragraphs only for an indent inside a list item
/// and a table cell's alignment.
///
/// <para>Raw HTML is never interpreted or imported: a tag prints as the literal text it is. Markdown
/// images print as their alternative text in brackets, only absolute http, https and mailto links
/// become hyperlinks, and a block type this class does not know prints its literal source text.</para>
///
/// <para>The prepared document's figures (<see cref="BenchmarkPdfMarkdownComposer.Prepared.Figures"/>)
/// become inline pictures with a caption; a figure marker without a chart prints nothing.</para>
/// </summary>
internal sealed class BenchmarkWordMarkdownWriter
{
    /// <summary>Code lines per cancellation check.</summary>
    private const int CodeChunkLines = 80;

    /// <summary>The first drawing id a figure takes: 1 is the title block's logo, 2 the running header's emblem.</summary>
    public const uint FirstFigureDrawingId = 3;

    /// <summary>English Metric Units per twip.</summary>
    private const long EmuPerTwip = 635;

    private readonly MainDocumentPart _part;
    private readonly BenchmarkPdfMarkdownComposer.Prepared _document;
    private readonly int _textWidth;
    private readonly int _maxFigureHeight;
    private readonly CancellationToken _token;
    private readonly Dictionary<string, string> _hyperlinks = new(StringComparer.Ordinal);
    private readonly List<BenchmarkWordOrderedList> _orderedLists = new();
    private int _nextNumberingId = WordStyles.FirstOrderedNumberingId;
    private int _nextBookmarkId;
    private uint _nextDrawingId = FirstFigureDrawingId;

    /// <summary>
    /// Where a block sits: the text indent of the list item it belongs to, in twips, whether it is
    /// inside a block quote, and how many lists enclose it.
    /// </summary>
    private sealed record Scope(int Indent, bool Quote, int ListDepth)
    {
        public static readonly Scope Body = new(0, false, 0);

        /// <summary>The left edge of the block's text: the list indent plus a quote's indent.</summary>
        public int Offset => Indent + (Quote ? WordStyles.Twips(10) : 0);
    }

    /// <param name="maxFigureHeight">The tallest a figure's picture may be, in twips; 0 for no cap.</param>
    public BenchmarkWordMarkdownWriter(
        MainDocumentPart part, BenchmarkPdfMarkdownComposer.Prepared document, int textWidth, CancellationToken token,
        int maxFigureHeight = 0)
    {
        _part = part;
        _document = document;
        _textWidth = textWidth;
        _token = token;
        _maxFigureHeight = maxFigureHeight;
    }

    /// <summary>Every ordered list written so far, each with its own numbering instance.</summary>
    public IReadOnlyList<BenchmarkWordOrderedList> OrderedLists => _orderedLists;

    /// <summary>The bookmark of a top-level <c>##</c> section, the target of its table-of-contents entry.</summary>
    public static string BookmarkName(string sectionId) => "_Toc_" + sectionId.Replace('-', '_');

    /// <summary>Appends the body's blocks to <paramref name="target"/>, checking the token between blocks.</summary>
    public void WriteBody(OpenXmlElement target) => Blocks(target, _document.Blocks, Scope.Body);

    // ---------------------------------------------------------------------------------------------
    // Table of contents
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A Word table of contents over the <c>##</c> sections: a content control in the "Table of
    /// Contents" gallery holding a real <c>TOC</c> field, marked dirty so Word refreshes it, page
    /// numbers included, when the file is opened. Its cached result already lists every section as a
    /// link to its bookmark, so the list works before any update.
    /// </summary>
    public static SdtBlock TableOfContents(BenchmarkPdfMarkdownComposer.Prepared document)
    {
        var content = new SdtContentBlock();
        content.Append(new Paragraph(Properties(WordStyles.TocHeading), new Run(TextOf("Contents"))));

        var entries = document.Contents;
        for (int i = 0; i < entries.Count; i++)
        {
            var (id, text) = entries[i];
            var paragraph = new Paragraph(Properties(WordStyles.Toc2));
            if (i == 0)
            {
                paragraph.Append(new Run(new FieldChar { FieldCharType = FieldCharValues.Begin, Dirty = true }));
                paragraph.Append(new Run(new FieldCode(" TOC \\o \"2-2\" \\h \\z \\u ") { Space = SpaceProcessingModeValues.Preserve }));
                paragraph.Append(new Run(new FieldChar { FieldCharType = FieldCharValues.Separate }));
            }
            paragraph.Append(new Hyperlink(new Run(TextOf(text))) { Anchor = BookmarkName(id), History = true });
            if (i == entries.Count - 1)
            {
                paragraph.Append(new Run(new FieldChar { FieldCharType = FieldCharValues.End }));
            }
            content.Append(paragraph);
        }

        return new SdtBlock(
            new SdtProperties(new SdtContentDocPartObject(
                new DocPartGallery { Val = "Table of Contents" },
                new DocPartUnique())),
            content);
    }

    // ---------------------------------------------------------------------------------------------
    // Blocks
    // ---------------------------------------------------------------------------------------------

    private void Blocks(OpenXmlElement target, IReadOnlyList<Block> blocks, Scope scope)
    {
        foreach (var block in blocks)
        {
            _token.ThrowIfCancellationRequested();

            switch (block)
            {
                case LinkReferenceDefinitionGroup:
                    break;
                case HeadingBlock heading:
                    target.Append(Heading(heading, scope));
                    break;
                case ParagraphBlock paragraph when _document.Figures.TryGetValue(paragraph, out var figure):
                    WriteFigure(target, figure, scope);
                    break;
                case ParagraphBlock paragraph when Composer.IsUndrawnMarker(paragraph, _document):
                    break;
                case ParagraphBlock paragraph:
                    target.Append(TextParagraph(paragraph.Inline, scope));
                    break;
                case MdTable table:
                    WriteTable(target, table, scope);
                    break;
                case CodeBlock code:
                    AppendCode(target, CodeLines(code.Lines.ToString()), CodeIndent(scope), _token);
                    break;
                case ListBlock list:
                    WriteList(target, list, scope);
                    break;
                case QuoteBlock quote:
                    Blocks(target, quote.ToList(), scope with { Quote = true });
                    break;
                case ThematicBreakBlock:
                    target.Append(new Paragraph(Properties(WordStyles.HorizontalRule, IndentOf(scope))));
                    break;
                case HtmlBlock html:
                    Literal(target, Composer.SourceOf(html, _document.Source), scope);
                    break;
                case ContainerBlock other:
                    Blocks(target, other.ToList(), scope);
                    break;
                default:
                    Literal(target, Composer.SourceOf(block, _document.Source), scope);
                    break;
            }
        }
    }

    /// <summary>A heading in <c>Heading1</c>…<c>Heading6</c>; a top-level <c>##</c> is wrapped in its section's bookmark.</summary>
    private Paragraph Heading(HeadingBlock heading, Scope scope)
    {
        var paragraph = new Paragraph(Properties(WordStyles.Heading(heading.Level), IndentOf(scope)));
        if (_document.SectionIds.TryGetValue(heading, out string? sectionId))
        {
            string id = (_nextBookmarkId++).ToString(CultureInfo.InvariantCulture);
            paragraph.Append(new BookmarkStart { Id = id, Name = BookmarkName(sectionId) });
            Inlines(paragraph, heading.Inline, default);
            paragraph.Append(new BookmarkEnd { Id = id });
        }
        else
        {
            Inlines(paragraph, heading.Inline, default);
        }
        return paragraph;
    }

    /// <summary>A Markdown paragraph: <c>Normal</c> in the body and in a table cell, <c>Quote</c> in a quote, <c>List Paragraph</c> in a list item.</summary>
    private Paragraph TextParagraph(ContainerInline? inline, Scope scope, JustificationValues? justification = null)
    {
        var paragraph = new Paragraph(Properties(ParagraphStyleOf(scope), IndentOf(scope), justification));
        Inlines(paragraph, inline, default);
        return paragraph;
    }

    /// <summary>Literal source text, its line breaks kept, in the style a paragraph would take.</summary>
    private static void Literal(OpenXmlElement target, string text, Scope scope)
    {
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd('\n');
        if (text.Length == 0) return;

        var paragraph = new Paragraph(Properties(ParagraphStyleOf(scope), IndentOf(scope)));
        string[] lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) paragraph.Append(new Run(new Break()));
            if (lines[i].Length > 0) paragraph.Append(new Run(TextOf(lines[i])));
        }
        target.Append(paragraph);
    }

    private static string? ParagraphStyleOf(Scope scope)
        => scope.Quote ? WordStyles.Quote : scope.Indent > 0 ? WordStyles.ListParagraph : null;

    /// <summary>The direct left indent of a block inside a list item; none elsewhere, where its style's applies.</summary>
    private static int? IndentOf(Scope scope) => scope.Indent > 0 ? scope.Offset : null;

    /// <summary>The left indent of a code block inside a list item or a quote; none in the body, where its style's applies.</summary>
    private static int? CodeIndent(Scope scope) => scope.Offset > 0 ? scope.Offset + WordStyles.Twips(6) : null;

    /// <summary>Code text as lines: line breaks normalized, tabs as four spaces.</summary>
    internal static string[] CodeLines(string code)
        => code.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\t", "    ", StringComparison.Ordinal)
            .Split('\n');

    /// <summary>
    /// One <c>Code Block</c> paragraph per line, leading spaces kept; Word draws consecutive lines in one
    /// box because their borders are identical. The token is checked between chunks of lines.
    /// </summary>
    internal static void AppendCode(OpenXmlElement target, IReadOnlyList<string> lines, int? indent, CancellationToken token)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            if (i % CodeChunkLines == 0) token.ThrowIfCancellationRequested();

            var properties = Properties(WordStyles.CodeBlock);
            if (indent != null)
            {
                properties.Indentation = new Indentation
                {
                    Left = indent.Value.ToString(CultureInfo.InvariantCulture),
                    Right = WordStyles.Twips(6).ToString(CultureInfo.InvariantCulture)
                };
            }
            var paragraph = new Paragraph(properties);
            if (lines[i].Length > 0) paragraph.Append(new Run(TextOf(lines[i])));
            target.Append(paragraph);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Figures
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A figure: the chart as an inline picture in its own PNG image part, centered, as wide as the text
    /// column unless its height reaches the cap, kept with the caption paragraph below it,
    /// "<b>Figure N.</b> <i>Title</i> — caption" in the secondary size.
    /// </summary>
    private void WriteFigure(OpenXmlElement target, Figure figure, Scope scope)
    {
        var chart = figure.Chart;
        var image = _part.AddImagePart(ImagePartType.Png);
        using (var stream = new MemoryStream(chart.Png, writable: false))
        {
            image.FeedData(stream);
        }

        int available = Math.Max(_textWidth / 4, _textWidth - scope.Offset);
        var (width, height) = Composer.FigureSize(
            chart.WidthPx, chart.HeightPx, (double)available * EmuPerTwip, (double)_maxFigureHeight * EmuPerTwip);

        uint id = _nextDrawingId++;
        string name = "Figure " + figure.Number.ToString(CultureInfo.InvariantCulture);

        var pictureProperties = Properties(null, IndentOf(scope), JustificationValues.Center);
        pictureProperties.KeepNext = new KeepNext();
        pictureProperties.SpacingBetweenLines = new SpacingBetweenLines
        {
            Before = WordStyles.Twips(4).ToString(CultureInfo.InvariantCulture),
            After = WordStyles.Twips(4).ToString(CultureInfo.InvariantCulture),
            Line = "240",
            LineRule = LineSpacingRuleValues.Auto
        };
        target.Append(new Paragraph(
            pictureProperties,
            new Run(Picture(_part.GetIdOfPart(image), id, name, Composer.AltTextOf(chart), (long)Math.Round(width), (long)Math.Round(height)))));

        var (label, title, caption) = Composer.CaptionParts(figure);
        var captionProperties = Properties(null, IndentOf(scope), JustificationValues.Center);
        captionProperties.KeepLines = new KeepLines();
        var captionParagraph = new Paragraph(captionProperties);
        captionParagraph.Append(CaptionRun(label, bold: true, italic: false));
        if (title.Length > 0)
        {
            captionParagraph.Append(CaptionRun(" " + title, bold: false, italic: true));
        }
        if (caption.Length > 0)
        {
            captionParagraph.Append(CaptionRun((title.Length > 0 ? " — " : " ") + caption, bold: false, italic: false));
        }
        target.Append(captionParagraph);
    }

    private static Run CaptionRun(string text, bool bold, bool italic)
    {
        var properties = new RunProperties
        {
            FontSize = WordStyles.Size(BenchmarkPdfStyle.TableCellSize),
            FontSizeComplexScript = WordStyles.SizeCs(BenchmarkPdfStyle.TableCellSize)
        };
        if (bold) properties.Bold = new Bold();
        if (italic) properties.Italic = new Italic();
        return new Run(properties, TextOf(text));
    }

    /// <summary>
    /// An inline PNG, <paramref name="description"/> its alternative text (empty for a decorative picture).
    /// <paramref name="id"/> must be unique among the document's drawings, headers included.
    /// </summary>
    internal static Drawing Picture(string relationshipId, uint id, string name, string description, long width, long height)
        => new(
            new DW.Inline(
                new DW.Extent { Cx = width, Cy = height },
                new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                new DW.DocProperties { Id = id, Name = name, Description = description },
                new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
                new A.Graphic(
                    new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties { Id = id, Name = name + ".png", Description = description },
                                new PIC.NonVisualPictureDrawingProperties()),
                            new PIC.BlipFill(
                                new A.Blip { Embed = relationshipId },
                                new A.Stretch(new A.FillRectangle())),
                            new PIC.ShapeProperties(
                                new A.Transform2D(
                                    new A.Offset { X = 0L, Y = 0L },
                                    new A.Extents { Cx = width, Cy = height }),
                                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
                    { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
            {
                DistanceFromTop = 0U,
                DistanceFromBottom = 0U,
                DistanceFromLeft = 0U,
                DistanceFromRight = 0U
            });

    // ---------------------------------------------------------------------------------------------
    // Lists
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A list: each item's first paragraph numbered at the list's level (bullet lists share one
    /// numbering instance; every ordered list gets its own, restarting at its first number), and the
    /// item's later blocks indented to its text. Inside a quote the items move in by the quote's indent.
    /// </summary>
    private void WriteList(OpenXmlElement target, ListBlock list, Scope scope)
    {
        int level = Math.Min(scope.ListDepth, 8);
        int numberingId;
        int indent;
        int hanging;
        if (list.IsOrdered)
        {
            int start = 1;
            if (!string.IsNullOrEmpty(list.OrderedStart)
                && int.TryParse(list.OrderedStart, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                start = parsed;
            }
            numberingId = _nextNumberingId++;
            _orderedLists.Add(new BenchmarkWordOrderedList(numberingId, level, start, list.OrderedDelimiter == ')' ? ')' : '.'));
            indent = WordStyles.OrderedIndent(level);
            hanging = WordStyles.OrderedIndent(0);
        }
        else
        {
            numberingId = WordStyles.BulletNumberingId;
            indent = WordStyles.BulletIndent(level);
            hanging = WordStyles.BulletIndent(0);
        }

        int quoteIndent = scope.Quote ? WordStyles.Twips(10) : 0;
        var inner = new Scope(indent + quoteIndent, false, scope.ListDepth + 1);

        foreach (var item in list.OfType<ListItemBlock>())
        {
            _token.ThrowIfCancellationRequested();
            var children = item.ToList();

            var properties = Properties(WordStyles.ListParagraph);
            properties.NumberingProperties = new NumberingProperties
            {
                NumberingLevelReference = new NumberingLevelReference { Val = level },
                NumberingId = new NumberingId { Val = numberingId }
            };
            if (quoteIndent > 0)
            {
                properties.Indentation = new Indentation
                {
                    Left = (indent + quoteIndent).ToString(CultureInfo.InvariantCulture),
                    Hanging = hanging.ToString(CultureInfo.InvariantCulture)
                };
            }
            var numbered = new Paragraph(properties);

            int first = 0;
            if (children.Count > 0 && children[0] is ParagraphBlock paragraph)
            {
                Inlines(numbered, paragraph.Inline, default);
                first = 1;
            }
            target.Append(numbered);

            Blocks(target, children.Skip(first).ToList(), inner);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Tables
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A pipe table in <c>GnollBench Table</c>: the full text width with its grid in the PDF's column
    /// proportions, header rows repeated on every page, body rows kept whole, and the cells of a numeric
    /// column or a column with a declared alignment justified to match.
    /// </summary>
    private void WriteTable(OpenXmlElement target, MdTable table, Scope scope)
    {
        var rows = table.OfType<MdTableRow>().ToList();
        if (rows.Count == 0) return;

        var cellsByRow = rows.Select(r => r.OfType<MdTableCell>().ToList()).ToList();
        int columns = Math.Max(1, cellsByRow.Max(cells => cells.Sum(c => Math.Max(1, c.ColumnSpan))));

        var headerRows = new List<List<MdTableCell>>();
        var bodyRows = new List<List<MdTableCell>>();
        for (int r = 0; r < rows.Count; r++)
        {
            (rows[r].IsHeader ? headerRows : bodyRows).Add(cellsByRow[r]);
        }

        var (aligns, weights) = Composer.ColumnLayout(table, headerRows, bodyRows, columns, _document.Source);

        int offset = scope.Offset;
        int width = Math.Max(_textWidth / 4, _textWidth - offset);
        int[] grid = Scale(weights, width);

        // Word merges two tables with nothing between them.
        if (target.LastChild is Table)
        {
            target.Append(new Paragraph());
        }

        var properties = new TableProperties
        {
            TableStyle = new TableStyle { Val = WordStyles.GnollBenchTable },
            TableWidth = offset == 0
                ? new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct }
                : new TableWidth { Width = width.ToString(CultureInfo.InvariantCulture), Type = TableWidthUnitValues.Dxa },
            TableLayout = new TableLayout { Type = TableLayoutValues.Autofit },
            TableLook = new TableLook
            {
                Val = "0420",
                FirstRow = true,
                LastRow = false,
                FirstColumn = false,
                LastColumn = false,
                NoHorizontalBand = false,
                NoVerticalBand = true
            }
        };
        if (offset > 0)
        {
            properties.TableIndentation = new TableIndentation { Width = offset, Type = TableWidthUnitValues.Dxa };
        }

        var result = new Table(properties, new TableGrid(grid.Select(w => new GridColumn { Width = w.ToString(CultureInfo.InvariantCulture) })));

        foreach (var cells in headerRows)
        {
            result.Append(Row(cells, columns, grid, aligns, header: true));
        }
        foreach (var cells in bodyRows)
        {
            _token.ThrowIfCancellationRequested();
            result.Append(Row(cells, columns, grid, aligns, header: false));
        }

        target.Append(result);
    }

    /// <summary>The PDF's relative column widths scaled to <paramref name="width"/> twips, summing to it exactly.</summary>
    private static int[] Scale(float[] weights, int width)
    {
        double total = weights.Sum(w => (double)w);
        int[] grid = weights.Select(w => (int)Math.Floor(w / total * width)).ToArray();
        grid[^1] += width - grid.Sum();
        return grid;
    }

    private TableRow Row(List<MdTableCell> cells, int columns, int[] grid, CellAlign[] aligns, bool header)
    {
        var properties = new TableRowProperties();
        properties.Append(header ? new TableHeader() : new CantSplit());
        var row = new TableRow(properties);

        int column = 0;
        foreach (var cell in cells)
        {
            if (column >= columns) break;
            int span = Math.Clamp(cell.ColumnSpan, 1, columns - column);
            row.Append(Cell(cell, column, span, grid, aligns[column]));
            column += span;
        }

        // A short row gets empty cells, so every row carries the full set of rules.
        while (column < columns)
        {
            row.Append(Cell(null, column, 1, grid, aligns[column]));
            column++;
        }
        return row;
    }

    private TableCell Cell(MdTableCell? cell, int column, int span, int[] grid, CellAlign align)
    {
        var properties = new TableCellProperties
        {
            TableCellWidth = new TableCellWidth
            {
                Width = grid.Skip(column).Take(span).Sum().ToString(CultureInfo.InvariantCulture),
                Type = TableWidthUnitValues.Dxa
            }
        };
        if (span > 1)
        {
            properties.GridSpan = new GridSpan { Val = span };
        }
        var result = new TableCell(properties);

        JustificationValues? justification = align switch
        {
            CellAlign.Right => JustificationValues.Right,
            CellAlign.Center => JustificationValues.Center,
            _ => null
        };

        if (cell != null && cell.Count == 1 && cell[0] is ParagraphBlock paragraph)
        {
            result.Append(TextParagraph(paragraph.Inline, Scope.Body, justification));
        }
        else if (cell != null && cell.Count > 0)
        {
            Blocks(result, cell.ToList(), Scope.Body);
        }

        // Every cell ends in a paragraph.
        if (result.LastChild is not Paragraph)
        {
            result.Append(new Paragraph(Properties(null, null, justification)));
        }
        return result;
    }

    // ---------------------------------------------------------------------------------------------
    // Inlines
    // ---------------------------------------------------------------------------------------------

    private void Inlines(OpenXmlElement paragraph, ContainerInline? container, InlineStyle style)
    {
        if (container == null) return;

        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    AppendRun(paragraph, literal.Content.ToString(), style);
                    break;
                case CodeInline code:
                    AppendRun(paragraph, code.Content, style, WordStyles.InlineCode);
                    break;
                case LineBreakInline lineBreak:
                    paragraph.Append(lineBreak.IsHard ? new Run(new Break()) : new Run(TextOf(" ")));
                    break;
                case HtmlEntityInline entity:
                    AppendRun(paragraph, entity.Transcoded.ToString(), style);
                    break;
                case HtmlInline html:
                    AppendRun(paragraph, html.Tag ?? string.Empty, style);
                    break;
                case AutolinkInline autolink:
                    Link(paragraph, autolink.Url, autolink.IsEmail ? "mailto:" + autolink.Url : autolink.Url, style);
                    break;
                case LinkInline image when image.IsImage:
                    string alt = Composer.PlainText(image);
                    AppendRun(paragraph, "[" + (alt.Length > 0 ? alt : "image") + "]", style);
                    break;
                case LinkInline link:
                    string label = Composer.PlainText(link);
                    Link(paragraph, label.Length > 0 ? label : link.Url ?? string.Empty, link.Url, style);
                    break;
                case EmphasisInline emphasis:
                    Inlines(paragraph, emphasis, Composer.Emphasis(style, emphasis));
                    break;
                case ContainerInline other:
                    Inlines(paragraph, other, style);
                    break;
                default:
                    AppendRun(paragraph, Composer.SourceOf(inline.Span, _document.Source), style);
                    break;
            }
        }
    }

    /// <summary>A hyperlink for an absolute http, https or mailto URL, in the <c>Hyperlink</c> style; plain text otherwise.</summary>
    private void Link(OpenXmlElement paragraph, string label, string? url, InlineStyle style)
    {
        if (!Composer.IsSafeUrl(url))
        {
            AppendRun(paragraph, label, style);
            return;
        }

        if (!_hyperlinks.TryGetValue(url!, out string? id))
        {
            id = _part.AddHyperlinkRelationship(new Uri(url!, UriKind.Absolute), true).Id;
            _hyperlinks[url!] = id;
        }

        var hyperlink = new Hyperlink { Id = id, History = true };
        AppendRun(hyperlink, label, style, WordStyles.Hyperlink);
        if (hyperlink.HasChildren)
        {
            paragraph.Append(hyperlink);
        }
    }

    private static void AppendRun(OpenXmlElement parent, string text, InlineStyle style, string? runStyle = null)
    {
        if (text.Length == 0) return;

        var run = new Run();
        var properties = RunPropertiesOf(style, runStyle);
        if (properties != null)
        {
            run.RunProperties = properties;
        }
        run.Append(TextOf(text));
        parent.Append(run);
    }

    /// <summary>The run's character style and the formatting its Markdown marks; null when it has neither.</summary>
    private static RunProperties? RunPropertiesOf(InlineStyle style, string? runStyle)
    {
        if (runStyle == null && style == default) return null;

        var properties = new RunProperties();
        if (runStyle != null) properties.RunStyle = new RunStyle { Val = runStyle };
        if (style.Bold) properties.Bold = new Bold();
        if (style.Italic) properties.Italic = new Italic();
        if (style.Strike) properties.Strike = new Strike();
        if (style.Underline) properties.Underline = new Underline { Val = UnderlineValues.Single };
        if (style.Marked) properties.Shading = WordStyles.Fill(Palette.MarkedBackground);
        return properties;
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------

    /// <summary>Paragraph properties with a style, a direct left indent and an alignment, each only when given.</summary>
    internal static ParagraphProperties Properties(string? style, int? indent = null, JustificationValues? justification = null)
    {
        var properties = new ParagraphProperties();
        if (style != null)
        {
            properties.ParagraphStyleId = new ParagraphStyleId { Val = style };
        }
        if (indent != null)
        {
            properties.Indentation = new Indentation { Left = indent.Value.ToString(CultureInfo.InvariantCulture) };
        }
        if (justification != null)
        {
            properties.Justification = new Justification { Val = justification.Value };
        }
        return properties;
    }

    /// <summary>A text element, its edge spaces preserved and any character XML cannot hold replaced.</summary>
    internal static Text TextOf(string value)
    {
        string safe = XmlSafe(value);
        var text = new Text(safe);
        if (safe.Length > 0 && (char.IsWhiteSpace(safe[0]) || char.IsWhiteSpace(safe[^1])))
        {
            text.Space = SpaceProcessingModeValues.Preserve;
        }
        return text;
    }

    /// <summary>The text with every character XML 1.0 cannot hold (a control character, a lone surrogate) replaced by U+FFFD.</summary>
    internal static string XmlSafe(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        StringBuilder? sb = null;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                sb?.Append(c).Append(value[i + 1]);
                i++;
                continue;
            }
            if (XmlConvert.IsXmlChar(c))
            {
                sb?.Append(c);
                continue;
            }
            sb ??= new StringBuilder(value.Length).Append(value, 0, i);
            sb.Append('�');
        }
        return sb?.ToString() ?? value;
    }
}
