namespace Overseer.Services.Benchmarking.Pdf;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Overseer.Models;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using ITableCellContainer = QuestPDF.Elements.Table.ITableCellContainer;
using MdTable = Markdig.Extensions.Tables.Table;
using MdTableCell = Markdig.Extensions.Tables.TableCell;
using MdTableColumnAlign = Markdig.Extensions.Tables.TableColumnAlign;
using MdTableRow = Markdig.Extensions.Tables.TableRow;

/// <summary>
/// Walks a Markdig syntax tree into QuestPDF elements for the body of a benchmark PDF. The page
/// frame (title block, running header, footer, watermark) is <see cref="BenchmarkPdfRenderer"/>'s;
/// the colors and sizes are <see cref="BenchmarkPdfStyle"/>'s.
///
/// <para>Raw HTML is never interpreted: the pipeline disables it, so a tag prints as the literal
/// text it is. Images print as their alternative text in brackets, only absolute http, https and
/// mailto links become clickable, and a block type this class does not know prints its literal
/// source text.</para>
///
/// <para>A top-level paragraph whose whole text is a figure marker (<c>[[figure:&lt;key&gt;]]</c>)
/// is drawn as a numbered figure when a chart for its key is supplied, and prints nothing
/// otherwise. Any other <c>[[…]]</c> text prints literally.</para>
/// </summary>
internal static class BenchmarkPdfMarkdownComposer
{
    // Strikethrough only among the extras: a single ~ or ^ is common prose in the reports
    // ("~5 s", "2^10") and must not turn into subscript or superscript.
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
        .UseAutoLinks()
        .DisableHtml()
        .Build();

    /// <summary>A cell value right-aligned in a column: a number, percentage, currency amount or duration.</summary>
    private static readonly Regex NumericCell = new(
        @"^[+\-−±]?\s?(?:US\$|[$€£])?\s?\d[\d,  ]*(?:\.\d+)?\s?(?:%|pp|pts?|ms|s|sec|min|m|h|x|×|k|M|USD|EUR)?(?:\s?±\s?\d[\d,]*(?:\.\d+)?\s?%?)?$"
        + @"|^\d+h\s?\d+m(?:\s?\d+(?:\.\d+)?s)?$"
        + @"|^\d+m\s?\d+(?:\.\d+)?s$"
        + @"|^\d{1,2}:\d{2}(?::\d{2}(?:\.\d+)?)?$",
        RegexOptions.CultureInvariant);

    /// <summary>Cell values that stand for "no value" and do not decide a column's alignment.</summary>
    private static readonly HashSet<string> Placeholders = new(StringComparer.OrdinalIgnoreCase)
    {
        "", "—", "–", "-", "N/A", "NA", "n.a.", "not available"
    };

    private static readonly string[] Bullets = { "•", "◦", "▪" };

    /// <summary>A paragraph's whole source text when it is a figure marker; group 1 is the figure key.</summary>
    private static readonly Regex FigureMarker = new(
        @"^\[\[figure:([A-Za-z0-9][A-Za-z0-9_.\-]*)\]\]$", RegexOptions.CultureInvariant);

    /// <summary>The largest share of the page's content height a figure's image may take.</summary>
    internal const double FigureMaxHeightShare = 0.6;

    // A lowercase letter or digit followed by an uppercase letter: where a zero-width space lets a
    // header such as ResultLengthChars wrap between its words.
    private static readonly Regex CamelCaseBreak = new(@"(\p{Ll}|\d)(\p{Lu})", RegexOptions.CultureInvariant);

    // Table column sizing, in estimated characters of the 9.5 pt cell text: the A4 text width
    // (170 mm), a cell's horizontal padding, and an average glyph advance of a little over half an em.
    private const double TableWidthPoints = 482;
    private const double CellPaddingPoints = 8;
    private const double AverageCharacterPoints = 5.2;
    private const double MonoWidthScale = 1.25;
    private const double SemiboldWidthScale = 1.08;
    private const int MaxColumnWordCharacters = 24;
    private const int MaxColumnTextCharacters = 60;

    // A table of at most this many columns whose preferred widths fill less than this share of the
    // text width is set at those widths, against the left margin, instead of across the page.
    private const int NarrowTableMaxColumns = 3;
    private const double NarrowTableMaxShare = 0.6;

    internal enum CellAlign
    {
        Left,
        Center,
        Right
    }

    internal readonly record struct InlineStyle(bool Bold, bool Italic, bool Strike, bool Underline, bool Marked);

    /// <summary>A parsed document, ready to compose: its top-level blocks and its table of contents.</summary>
    internal sealed class Prepared
    {
        public required string Source { get; init; }

        /// <summary>Top-level blocks, without a first <c>#</c> heading that repeats the title.</summary>
        public required IReadOnlyList<Block> Blocks { get; init; }

        /// <summary>The QuestPDF section name of every top-level <c>##</c> heading.</summary>
        public required IReadOnlyDictionary<HeadingBlock, string> SectionIds { get; init; }

        /// <summary>The top-level <c>##</c> headings in order, as section name and plain text.</summary>
        public required IReadOnlyList<(string Id, string Text)> Contents { get; init; }

        /// <summary>Each top-level figure marker paragraph that has a chart, with its figure number.</summary>
        public IReadOnlyDictionary<ParagraphBlock, Figure> Figures { get; init; } = new Dictionary<ParagraphBlock, Figure>();

        /// <summary>The drawn figures in order of appearance.</summary>
        public IReadOnlyList<Figure> OrderedFigures => Figures.Values.OrderBy(f => f.Number).ToList();
    }

    /// <summary>A chart drawn at a figure marker, numbered from 1 in order of appearance.</summary>
    internal sealed record Figure(int Number, BenchmarkReportRenderChart Chart);

    /// <summary>
    /// The room a figure's image may take, in points: the text column's width and the height cap; and
    /// the page's height between its margins, 0 when unknown.
    /// </summary>
    internal readonly record struct FigureFrame(float Width, float MaxHeight, float PageHeight = 0);

    /// <summary><paramref name="CamelCaseBreaks"/>: a table header, whose identifiers may wrap between words.</summary>
    private sealed record Context(Prepared Document, CancellationToken Token, FigureFrame Frame, bool CamelCaseBreaks = false);

    public static MarkdownDocument Parse(string markdown) => Markdown.Parse(markdown ?? string.Empty, Pipeline);

    /// <summary>
    /// Parses <paramref name="markdown"/> and drops its first <c>#</c> heading when it repeats <paramref name="title"/>.
    /// Each top-level figure marker whose key has a chart in <paramref name="charts"/> becomes a figure.
    /// </summary>
    public static Prepared Prepare(string markdown, string title, IReadOnlyList<BenchmarkReportRenderChart>? charts = null)
    {
        string source = markdown ?? string.Empty;
        var document = Parse(source);
        var blocks = document.ToList();

        var firstHeading = blocks.OfType<HeadingBlock>().FirstOrDefault(h => h.Level == 1);
        if (firstHeading != null
            && string.Equals(PlainText(firstHeading.Inline), (title ?? string.Empty).Trim(), StringComparison.Ordinal))
        {
            blocks.Remove(firstHeading);
        }

        var sectionIds = new Dictionary<HeadingBlock, string>();
        var contents = new List<(string Id, string Text)>();
        foreach (var heading in blocks.OfType<HeadingBlock>().Where(h => h.Level == 2))
        {
            string id = "gb-section-" + (contents.Count + 1).ToString(CultureInfo.InvariantCulture);
            sectionIds[heading] = id;
            contents.Add((id, PlainText(heading.Inline)));
        }

        var figures = new Dictionary<ParagraphBlock, Figure>();
        if (charts is { Count: > 0 })
        {
            var byKey = new Dictionary<string, BenchmarkReportRenderChart>(StringComparer.Ordinal);
            foreach (var chart in charts)
            {
                if (chart != null && chart.Png.Length > 0 && chart.WidthPx > 0 && chart.HeightPx > 0)
                {
                    byKey.TryAdd(chart.FigureKey, chart);
                }
            }

            foreach (var paragraph in blocks.OfType<ParagraphBlock>())
            {
                if (FigureKeyOf(paragraph, source) is string key && byKey.TryGetValue(key, out var chart))
                {
                    figures[paragraph] = new Figure(figures.Count + 1, chart);
                }
            }
        }

        return new Prepared
        {
            Source = source,
            Blocks = blocks,
            SectionIds = sectionIds,
            Contents = contents,
            Figures = figures
        };
    }

    /// <summary>The figure key when the paragraph's whole text is a figure marker; null otherwise.</summary>
    internal static string? FigureKeyOf(ParagraphBlock paragraph, string source)
    {
        var match = FigureMarker.Match(SourceOf(paragraph, source).Trim());
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>A figure marker with no chart to draw, which prints nothing.</summary>
    internal static bool IsUndrawnMarker(ParagraphBlock paragraph, Prepared document)
        => !document.Figures.ContainsKey(paragraph) && FigureKeyOf(paragraph, document.Source) != null;

    /// <summary>
    /// An image of <paramref name="widthPx"/> by <paramref name="heightPx"/> scaled to <paramref name="maxWidth"/>,
    /// then scaled down proportionally when its height passes <paramref name="maxHeight"/> (no cap when 0 or less).
    /// </summary>
    internal static (double Width, double Height) FigureSize(int widthPx, int heightPx, double maxWidth, double maxHeight)
    {
        double ratio = widthPx > 0 && heightPx > 0 ? (double)heightPx / widthPx : 1.0;
        double width = maxWidth;
        double height = width * ratio;
        if (maxHeight > 0 && height > maxHeight)
        {
            height = maxHeight;
            width = height / ratio;
        }
        return (width, height);
    }

    /// <summary>The caption's parts: "Figure N.", the title, and the caption text, each trimmed.</summary>
    internal static (string Label, string Title, string Caption) CaptionParts(Figure figure)
        => ("Figure " + figure.Number.ToString(CultureInfo.InvariantCulture) + ".",
            (figure.Chart.Title ?? string.Empty).Trim(),
            (figure.Chart.Caption ?? string.Empty).Trim());

    /// <summary>The chart's alternative text, falling back to its title and then to "Chart".</summary>
    internal static string AltTextOf(BenchmarkReportRenderChart chart)
        => !string.IsNullOrWhiteSpace(chart.AltText) ? chart.AltText.Trim()
            : !string.IsNullOrWhiteSpace(chart.Title) ? chart.Title.Trim()
            : "Chart";

    /// <summary>The <c>##</c> sections with their page numbers, each a link to its section.</summary>
    public static void ComposeTableOfContents(IContainer container, Prepared document)
    {
        container.SemanticTableOfContents().Column(col =>
        {
            col.Spacing(2);
            BenchmarkPdfStyle.SemiboldSpan(col.Item().PaddingBottom(2).Text("Contents"))
                .FontSize(BenchmarkPdfStyle.Heading3Size)
                .FontColor(BenchmarkPdfStyle.Teal);

            foreach (var (id, text) in document.Contents)
            {
                col.Item().SemanticTableOfContentsItem().SectionLink(id).Row(row =>
                {
                    row.RelativeItem().Text(text).FontColor(BenchmarkPdfStyle.Teal);
                    row.AutoItem().PaddingLeft(8).Text(t =>
                    {
                        t.AlignRight();
                        t.BeginPageNumberOfSection(id);
                    });
                });
            }

            col.Item().PaddingTop(4).LineHorizontal(BenchmarkPdfStyle.Hairline).LineColor(BenchmarkPdfStyle.Gold);
        });
    }

    /// <summary>
    /// The body, its figures sized within <paramref name="frame"/>. The last <c>##</c> section is kept on
    /// one page where <see cref="KeptTogetherSectionStart"/> finds it fits; otherwise it flows as the
    /// rest of the body does. The token is checked between blocks, so a canceled request stops composing.
    /// </summary>
    public static void ComposeBody(IContainer container, Prepared document, CancellationToken cancellationToken, FigureFrame frame)
    {
        var ctx = new Context(document, cancellationToken, frame);
        int? keptStart = KeptTogetherSectionStart(document, frame);
        container.Column(col =>
        {
            col.Spacing(BenchmarkPdfStyle.BlockSpacing);
            if (keptStart is not int start)
            {
                Blocks(col, document.Blocks, ctx, 0);
                return;
            }

            Blocks(col, document.Blocks.Take(start).ToList(), ctx, 0);
            col.Item().PreventPageBreak().Column(section =>
            {
                section.Spacing(BenchmarkPdfStyle.BlockSpacing);
                Blocks(section, document.Blocks.Skip(start).ToList(), ctx, 0);
            });
        });
    }

    /// <summary>
    /// Room the running header and the footer take from a page's height between its margins, in points,
    /// with a margin for error.
    /// </summary>
    internal const float PageFrameAllowance = 72f;

    /// <summary>
    /// The index in <see cref="Prepared.Blocks"/> of the last top-level <c>##</c> heading, whose section
    /// runs to the end of the body, when that section is kept on one page: the body has at least two
    /// <c>##</c> sections, and the section's <see cref="EstimatedHeight"/> fits in the page's height
    /// less <see cref="PageFrameAllowance"/>. Null otherwise, and when the page height is unknown.
    /// </summary>
    internal static int? KeptTogetherSectionStart(Prepared document, FigureFrame frame)
    {
        if (frame.PageHeight <= 0 || document.Contents.Count < 2) return null;

        int start = -1;
        for (int i = 0; i < document.Blocks.Count; i++)
        {
            if (document.Blocks[i] is HeadingBlock { Level: 2 }) start = i;
        }
        if (start < 0) return null;

        float height = 0;
        for (int i = start; i < document.Blocks.Count; i++)
        {
            height += EstimatedHeight(document.Blocks[i], document, frame) + BenchmarkPdfStyle.BlockSpacing;
        }
        return height <= frame.PageHeight - PageFrameAllowance ? start : null;
    }

    /// <summary>
    /// A generous estimate of a top-level block's height, in points: a heading at its own height, a
    /// drawn figure at its image's height cap and a caption, a table at two lines of cell text per
    /// source line, and any other block at one line of body text per <see cref="EstimatedLineCharacters"/>
    /// characters of each source line. It errs high, so a section it keeps together fits.
    /// </summary>
    internal static float EstimatedHeight(Block block, Prepared document, FigureFrame frame)
    {
        switch (block)
        {
            case HeadingBlock heading:
                return HeadingHeight(heading.Level);
            case ParagraphBlock paragraph when document.Figures.ContainsKey(paragraph):
                return frame.MaxHeight + 3 * BenchmarkPdfStyle.BaseSize * BenchmarkPdfStyle.LineHeight;
            case ParagraphBlock paragraph when IsUndrawnMarker(paragraph, document):
                return 0;
        }

        float lineHeight = block is MdTable
            ? 2 * BenchmarkPdfStyle.TableCellSize * BenchmarkPdfStyle.LineHeight
            : BenchmarkPdfStyle.BaseSize * BenchmarkPdfStyle.LineHeight;

        int lines = 0;
        foreach (string line in SourceOf(block, document.Source).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            lines += Math.Max(1, (int)Math.Ceiling(line.Length / (double)EstimatedLineCharacters));
        }
        return lines * lineHeight;
    }

    /// <summary>Characters of body text <see cref="EstimatedHeight"/> counts to a line: fewer than the text width holds.</summary>
    internal const int EstimatedLineCharacters = 80;

    // ---------------------------------------------------------------------------------------------
    // Blocks
    // ---------------------------------------------------------------------------------------------

    private static void Blocks(ColumnDescriptor col, IReadOnlyList<Block> blocks, Context ctx, int listDepth)
    {
        for (int i = 0; i < blocks.Count; i++)
        {
            ctx.Token.ThrowIfCancellationRequested();
            var block = blocks[i];

            if (block is LinkReferenceDefinitionGroup
                || (block is ParagraphBlock marker && IsUndrawnMarker(marker, ctx.Document)))
            {
                continue;
            }

            if (block is HeadingBlock)
            {
                // A run of consecutive headings keeps with what follows: a first paragraph moves with
                // it when the group does not fit, and before anything else the run needs room on its
                // page for its own height and the first lines of the next block.
                int last = i;
                while (last + 1 < blocks.Count && blocks[last + 1] is HeadingBlock) last++;
                var headings = blocks.Skip(i).Take(last - i + 1).Cast<HeadingBlock>().ToList();
                var next = last + 1 < blocks.Count ? blocks[last + 1] : null;

                if (next is ParagraphBlock paragraph && !IsUndrawnMarker(paragraph, ctx.Document))
                {
                    col.Item().PreventPageBreak().Column(group =>
                    {
                        group.Spacing(BenchmarkPdfStyle.BlockSpacing);
                        foreach (var heading in headings) group.Item().Element(c => Heading(c, heading, ctx));
                        group.Item().Element(c => ParagraphOrFigure(c, paragraph, ctx));
                    });
                    i = last + 1;
                }
                else
                {
                    col.Item().EnsureSpace(KeepWithNextHeight(headings, next)).Column(group =>
                    {
                        group.Spacing(BenchmarkPdfStyle.BlockSpacing);
                        foreach (var heading in headings) group.Item().Element(c => Heading(c, heading, ctx));
                    });
                    i = last;
                }
                continue;
            }

            col.Item().Element(c => ComposeBlock(c, block, ctx, listDepth));
        }
    }

    /// <summary>
    /// The room, in points, a run of headings needs on its page: each heading's own height with the
    /// spacing between blocks, and then <see cref="BenchmarkPdfStyle.KeepWithTableHeight"/> before a
    /// table, <see cref="BenchmarkPdfStyle.KeepWithNextHeight"/> before any other block, and nothing
    /// at the end of the body.
    /// </summary>
    internal static float KeepWithNextHeight(IReadOnlyList<HeadingBlock> headings, Block? next)
    {
        float height = headings.Sum(h => HeadingHeight(h.Level)) + (headings.Count - 1) * BenchmarkPdfStyle.BlockSpacing;
        if (next == null) return height;

        return height + BenchmarkPdfStyle.BlockSpacing
            + (next is MdTable ? BenchmarkPdfStyle.KeepWithTableHeight : BenchmarkPdfStyle.KeepWithNextHeight);
    }

    /// <summary>A one-line heading's height at its level: its padding, text line and, at level 2, its rule.</summary>
    internal static float HeadingHeight(int level) => level switch
    {
        1 => 8 + BenchmarkPdfStyle.Heading1Size * 1.2f,
        2 => 8 + BenchmarkPdfStyle.Heading2Size * 1.2f + 2 + 0.75f,
        3 => 4 + BenchmarkPdfStyle.Heading3Size * 1.2f,
        _ => 2 + BenchmarkPdfStyle.BaseSize * 1.2f
    };

    private static void ComposeBlock(IContainer container, Block block, Context ctx, int listDepth)
    {
        switch (block)
        {
            case HeadingBlock heading:
                Heading(container, heading, ctx);
                break;
            case ParagraphBlock paragraph:
                ParagraphOrFigure(container, paragraph, ctx);
                break;
            case MdTable table:
                ComposeTable(container, table, ctx);
                break;
            case CodeBlock code:
                Code(container, code.Lines.ToString());
                break;
            case ListBlock list:
                ComposeList(container, list, ctx, listDepth);
                break;
            case QuoteBlock quote:
                container.SemanticBlockQuotation()
                    .BorderLeft(2f).BorderColor(BenchmarkPdfStyle.QuoteRule)
                    .PaddingLeft(10).PaddingVertical(1)
                    .DefaultTextStyle(s => s.FontColor(BenchmarkPdfStyle.Muted))
                    .Column(col =>
                    {
                        col.Spacing(4);
                        Blocks(col, quote.ToList(), ctx, listDepth);
                    });
                break;
            case ThematicBreakBlock:
                container.PaddingVertical(4).LineHorizontal(BenchmarkPdfStyle.Hairline).LineColor(BenchmarkPdfStyle.Rule);
                break;
            case HtmlBlock html:
                Literal(container, SourceOf(html, ctx.Document.Source));
                break;
            case ContainerBlock other:
                container.Column(col =>
                {
                    col.Spacing(BenchmarkPdfStyle.BlockSpacing);
                    Blocks(col, other.ToList(), ctx, listDepth);
                });
                break;
            default:
                Literal(container, SourceOf(block, ctx.Document.Source));
                break;
        }
    }

    private static void Heading(IContainer container, HeadingBlock heading, Context ctx)
    {
        IContainer target = ctx.Document.SectionIds.TryGetValue(heading, out string? id) ? container.Section(id) : container;

        switch (heading.Level)
        {
            case 1:
                target.PaddingTop(8).SemanticHeading1()
                    .Text(t => HeadingText(t, heading, BenchmarkPdfStyle.Heading1Size, ctx));
                break;
            case 2:
                target.PaddingTop(8)
                    .BorderBottom(0.75f).BorderColor(BenchmarkPdfStyle.Gold)
                    .PaddingBottom(2)
                    .SemanticHeading2()
                    .Text(t => HeadingText(t, heading, BenchmarkPdfStyle.Heading2Size, ctx));
                break;
            case 3:
                target.PaddingTop(4).SemanticHeading3()
                    .Text(t => HeadingText(t, heading, BenchmarkPdfStyle.Heading3Size, ctx));
                break;
            case 4:
                target.PaddingTop(2).SemanticHeading4()
                    .Text(t => HeadingText(t, heading, BenchmarkPdfStyle.BaseSize, ctx));
                break;
            case 5:
                target.PaddingTop(2).SemanticHeading5()
                    .Text(t => HeadingText(t, heading, BenchmarkPdfStyle.BaseSize, ctx));
                break;
            default:
                target.PaddingTop(2).SemanticHeading6()
                    .Text(t => HeadingText(t, heading, BenchmarkPdfStyle.BaseSize, ctx));
                break;
        }
    }

    private static void HeadingText(TextDescriptor text, HeadingBlock heading, float size, Context ctx)
    {
        text.DefaultTextStyle(s => BenchmarkPdfStyle.Semibold(s)
            .FontSize(size)
            .LineHeight(1.2f)
            .FontColor(BenchmarkPdfStyle.Teal));
        Inlines(text, heading.Inline, default, ctx);
    }

    private static void Paragraph(IContainer container, ContainerInline? inline, Context ctx, CellAlign align)
    {
        container.Text(t =>
        {
            Align(t, align);
            Inlines(t, inline, default, ctx);
        });
    }

    /// <summary>A figure when the paragraph is a marker with a chart, else the paragraph; an undrawn marker prints nothing.</summary>
    private static void ParagraphOrFigure(IContainer container, ParagraphBlock paragraph, Context ctx)
    {
        if (ctx.Document.Figures.TryGetValue(paragraph, out var figure))
        {
            ComposeFigure(container, figure, ctx);
        }
        else if (!IsUndrawnMarker(paragraph, ctx.Document))
        {
            Paragraph(container, paragraph.Inline, ctx, CellAlign.Left);
        }
    }

    /// <summary>
    /// A tagged figure: the chart image, centered, as wide as the text column unless its height reaches
    /// the frame's cap, and below it the caption "<b>Figure N.</b> <i>Title</i> — caption" in the
    /// secondary size, kept on one page with the image.
    /// </summary>
    private static void ComposeFigure(IContainer container, Figure figure, Context ctx)
    {
        var chart = figure.Chart;
        var (width, height) = FigureSize(chart.WidthPx, chart.HeightPx, ctx.Frame.Width, ctx.Frame.MaxHeight);
        var (label, title, caption) = CaptionParts(figure);

        container.PaddingVertical(4).PreventPageBreak().Column(col =>
        {
            col.Spacing(4);
            col.Item().AlignCenter()
                .Width((float)width).Height((float)height)
                .SemanticFigure(AltTextOf(chart))
                .Image(chart.Png)
                .FitArea();
            col.Item().SemanticCaption().Text(t =>
            {
                t.AlignCenter();
                t.DefaultTextStyle(s => s.FontSize(BenchmarkPdfStyle.TableCellSize).LineHeight(1.3f));
                t.Span(label).Bold();
                if (title.Length > 0) t.Span(" " + title).Italic();
                if (caption.Length > 0) t.Span((title.Length > 0 ? " — " : " ") + caption);
            });
        });
    }

    /// <summary>Monospace on a light tint with a hairline border, wrapped anywhere, line breaks kept.</summary>
    internal static void Code(IContainer container, string code)
    {
        container.Background(BenchmarkPdfStyle.CodeBackground)
            .Border(BenchmarkPdfStyle.Hairline, BenchmarkPdfStyle.CodeBorder)
            .PaddingVertical(4).PaddingHorizontal(6)
            .Text(t =>
            {
                t.DefaultTextStyle(s => BenchmarkPdfStyle.Mono(s)
                    .FontSize(BenchmarkPdfStyle.CodeSize)
                    .LineHeight(BenchmarkPdfStyle.CodeLineHeight));
                t.Span(code.Replace("\t", "    ", StringComparison.Ordinal)).BreakAnywhere();
            });
    }

    private static void Literal(IContainer container, string text)
    {
        if (text.Length == 0) return;
        container.Text(t => t.Span(text));
    }

    private static void ComposeList(IContainer container, ListBlock list, Context ctx, int listDepth)
    {
        int start = 1;
        if (list.IsOrdered && !string.IsNullOrEmpty(list.OrderedStart)
            && int.TryParse(list.OrderedStart, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            start = parsed;
        }
        string bullet = Bullets[listDepth % Bullets.Length];
        float labelWidth = list.IsOrdered ? 22f : 12f;

        container.SemanticList().Column(col =>
        {
            col.Spacing(3);
            int index = 0;
            foreach (var item in list.OfType<ListItemBlock>())
            {
                ctx.Token.ThrowIfCancellationRequested();
                string label = list.IsOrdered
                    ? (start + index).ToString(CultureInfo.InvariantCulture) + list.OrderedDelimiter
                    : bullet;
                var children = item.ToList();

                col.Item().SemanticListItem().Row(row =>
                {
                    row.ConstantItem(labelWidth).SemanticListLabel().Text(label);
                    row.RelativeItem().SemanticListItemBody().Column(body =>
                    {
                        body.Spacing(4);
                        Blocks(body, children, ctx, listDepth + 1);
                    });
                });
                index++;
            }
        });
    }

    // ---------------------------------------------------------------------------------------------
    // Tables
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A pipe table as a decoration: the header row above the body, repeated on every page, and the
    /// body as a column of one-row tables sharing the header's column definitions, zebra-striped with
    /// hairline rules. Each body row is kept on one page when it fits on one, so a row's cells always
    /// start on the same page; a row taller than a page breaks across pages. A column whose non-empty
    /// body cells are all numbers, percentages, currency amounts or durations is right-aligned unless
    /// the Markdown sets its alignment, and a narrow table (<see cref="PdfColumnLayout"/>) is set at
    /// its preferred widths against the left margin.
    /// </summary>
    private static void ComposeTable(IContainer container, MdTable table, Context ctx)
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

        var (aligns, widths, constant) = PdfColumnLayout(table, headerRows, bodyRows, columns, ctx.Document.Source);

        void Columns(TableDescriptor t) => t.ColumnsDefinition(cd =>
        {
            foreach (float width in widths)
            {
                if (constant) cd.ConstantColumn(width);
                else cd.RelativeColumn(width);
            }
        });

        (constant ? container.AlignLeft() : container).SemanticTable().Decoration(d =>
        {
            if (headerRows.Count > 0)
            {
                d.Before().Table(t =>
                {
                    Columns(t);
                    t.Header(h =>
                    {
                        for (int r = 0; r < headerRows.Count; r++)
                        {
                            PlaceRow(() => h.Cell(), headerRows[r], (uint)(r + 1), columns, aligns, header: true, zebra: false, ctx);
                        }
                    });
                });
            }

            d.Content().Column(col =>
            {
                for (int r = 0; r < bodyRows.Count; r++)
                {
                    ctx.Token.ThrowIfCancellationRequested();
                    var cells = bodyRows[r];
                    bool zebra = r % 2 == 1;
                    col.Item().PreventPageBreak().Table(t =>
                    {
                        Columns(t);
                        PlaceRow(() => t.Cell(), cells, 1, columns, aligns, header: false, zebra, ctx);
                    });
                }
            });
        });
    }

    /// <summary>
    /// <see cref="ColumnLayout"/> for the PDF: relative weights across the text width, or, for a
    /// table of at most <see cref="NarrowTableMaxColumns"/> columns whose preferred widths fill less
    /// than <see cref="NarrowTableMaxShare"/> of it, constant widths in points at those preferred widths.
    /// </summary>
    internal static (CellAlign[] Aligns, float[] Widths, bool Constant) PdfColumnLayout(
        MdTable table, IReadOnlyList<List<MdTableCell>> headerRows, IReadOnlyList<List<MdTableCell>> bodyRows,
        int columns, string source)
    {
        var (aligns, bodyMinimum, minimum, preferred) = MeasureColumns(table, headerRows, bodyRows, columns, source);

        var preferredPoints = preferred.Select(w => (float)(w * AverageCharacterPoints + CellPaddingPoints)).ToArray();
        if (columns <= NarrowTableMaxColumns && preferredPoints.Sum() < TableWidthPoints * NarrowTableMaxShare)
        {
            return (aligns, preferredPoints, true);
        }

        return (aligns, ColumnWeights(bodyMinimum, minimum, preferred), false);
    }

    /// <summary>
    /// Each column's alignment and relative width, in points for the A4 text width: a declared
    /// alignment, else right for a numeric column and left otherwise; the widths from
    /// <see cref="ColumnWeights"/> over the columns' word and text lengths.
    /// </summary>
    internal static (CellAlign[] Aligns, float[] Weights) ColumnLayout(
        MdTable table, IReadOnlyList<List<MdTableCell>> headerRows, IReadOnlyList<List<MdTableCell>> bodyRows,
        int columns, string source)
    {
        var (aligns, bodyMinimum, minimum, preferred) = MeasureColumns(table, headerRows, bodyRows, columns, source);
        return (aligns, ColumnWeights(bodyMinimum, minimum, preferred));
    }

    /// <summary>
    /// Each column's alignment, and its body's longest word, longest word with the header's, and
    /// preferred width, in estimated characters.
    /// </summary>
    private static (CellAlign[] Aligns, double[] BodyMinimum, double[] Minimum, double[] Preferred) MeasureColumns(
        MdTable table, IReadOnlyList<List<MdTableCell>> headerRows, IReadOnlyList<List<MdTableCell>> bodyRows,
        int columns, string source)
    {
        var texts = new Dictionary<MdTableCell, string>();
        string TextOf(MdTableCell cell)
        {
            if (!texts.TryGetValue(cell, out string? value))
            {
                value = CellText(cell, source);
                texts[cell] = value;
            }
            return value;
        }

        var aligns = new CellAlign[columns];
        var bodyMinimum = new double[columns];
        var minimum = new double[columns];
        var preferred = new double[columns];
        for (int c = 0; c < columns; c++)
        {
            var bodyValues = bodyRows.Select(cells => CellAt(cells, c)).Where(cell => cell != null).Select(cell => TextOf(cell!)).ToList();
            var allValues = headerRows.Concat(bodyRows).Select(cells => CellAt(cells, c)).Where(cell => cell != null).Select(cell => TextOf(cell!));

            MdTableColumnAlign? declared = c < table.ColumnDefinitions.Count ? table.ColumnDefinitions[c].Alignment : null;
            aligns[c] = declared switch
            {
                MdTableColumnAlign.Right => CellAlign.Right,
                MdTableColumnAlign.Center => CellAlign.Center,
                MdTableColumnAlign.Left => CellAlign.Left,
                _ => IsNumericColumn(bodyValues) ? CellAlign.Right : CellAlign.Left
            };

            var values = allValues.ToList();
            // Word lengths scaled to regular-weight characters: monospace code is wider, and so is the
            // semibold header, whose identifiers may wrap at their CamelCase boundaries.
            var bodyWords = bodyRows.Select(cells => CellAt(cells, c)).Where(cell => cell != null)
                .Select(cell => (Text: TextOf(cell!), Scale: cell!.Descendants<CodeInline>().Any() ? MonoWidthScale : 1.0));
            var headerWords = headerRows.Select(cells => CellAt(cells, c)).Where(cell => cell != null)
                .Select(cell => (Text: CamelCaseBreak.Replace(TextOf(cell!), "$1 $2"), Scale: SemiboldWidthScale));
            static double LongestWord(IEnumerable<(string Text, double Scale)> texts) => texts
                .SelectMany(v => v.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(w => w.Length * v.Scale))
                .DefaultIfEmpty(1)
                .Max();
            static double Fit(double characters) => Math.Clamp(Math.Ceiling(characters) + 1, 3, MaxColumnWordCharacters);

            int longest = values.Select(v => v.Length).DefaultIfEmpty(1).Max();
            bodyMinimum[c] = Fit(LongestWord(bodyWords));
            minimum[c] = Math.Max(bodyMinimum[c], Fit(LongestWord(headerWords)));
            preferred[c] = Math.Max(minimum[c], Math.Min(longest, MaxColumnTextCharacters));
        }

        return (aligns, bodyMinimum, minimum, preferred);
    }

    /// <summary>
    /// Relative column widths in the manner of an automatic HTML table layout, in estimated characters.
    /// Every column gets its longest word, header words included, when the page can hold them all, and
    /// the width left over is shared in proportion to how much longer each column's longest text is.
    /// When it cannot, the body's longest words come first and the header's share what remains, so a
    /// value never breaks inside a word before a column heading does. When even the body's words do not
    /// fit, the columns are sized by them, so words break as evenly as they can.
    /// </summary>
    internal static float[] ColumnWeights(double[] bodyMinimum, double[] minimum, double[] preferred)
    {
        int columns = minimum.Length;
        double capacity = Math.Max(columns * 3.0, (TableWidthPoints - columns * CellPaddingPoints) / AverageCharacterPoints);

        double[] Share(double[] low, double[] high)
        {
            double lowSum = low.Sum(), highSum = high.Sum();
            return Enumerable.Range(0, columns)
                .Select(c => highSum <= capacity ? high[c]
                    : lowSum < capacity ? low[c] + (high[c] - low[c]) * (capacity - lowSum) / (highSum - lowSum)
                    : low[c])
                .ToArray();
        }

        double[] widths = minimum.Sum() <= capacity ? Share(minimum, preferred) : Share(bodyMinimum, minimum);

        // In points with the padding, since every cell loses the same padding whatever its share.
        return widths.Select(w => (float)(w * AverageCharacterPoints + CellPaddingPoints)).ToArray();
    }

    internal static MdTableCell? CellAt(List<MdTableCell> cells, int column)
    {
        int position = 0;
        foreach (var cell in cells)
        {
            int span = Math.Max(1, cell.ColumnSpan);
            if (column >= position && column < position + span) return position == column ? cell : null;
            position += span;
        }
        return null;
    }

    private static void PlaceRow(
        Func<ITableCellContainer> newCell, List<MdTableCell> cells, uint row, int columns, CellAlign[] aligns,
        bool header, bool zebra, Context ctx)
    {
        uint column = 1;
        foreach (var cell in cells)
        {
            if (column > columns) break;
            uint span = (uint)Math.Clamp(cell.ColumnSpan, 1, columns - (int)column + 1);
            var slot = newCell().Row(row).Column(column);
            if (span > 1) slot = slot.ColumnSpan(span);
            CellBody(slot, cell, aligns[column - 1], header, zebra, ctx);
            column += span;
        }

        // A short row gets empty cells, so every row carries the full set of rules.
        while (column <= columns)
        {
            CellBody(newCell().Row(row).Column(column), null, aligns[column - 1], header, zebra, ctx);
            column++;
        }
    }

    private static void CellBody(IContainer slot, MdTableCell? cell, CellAlign align, bool header, bool zebra, Context ctx)
    {
        IContainer c = slot;
        if (header) c = c.Background(BenchmarkPdfStyle.TableHeader);
        else if (zebra) c = c.Background(BenchmarkPdfStyle.Zebra);

        c = c.Border(BenchmarkPdfStyle.Hairline, BenchmarkPdfStyle.Rule)
            .PaddingVertical(2.5f).PaddingHorizontal(4)
            .DefaultTextStyle(s => header
                ? BenchmarkPdfStyle.Semibold(s).FontSize(BenchmarkPdfStyle.TableCellSize).LineHeight(1.25f)
                : s.FontSize(BenchmarkPdfStyle.TableCellSize).LineHeight(1.25f));

        if (cell == null || cell.Count == 0) return;

        if (cell.Count == 1 && cell[0] is ParagraphBlock paragraph)
        {
            Paragraph(c, paragraph.Inline, header ? ctx with { CamelCaseBreaks = true } : ctx, align);
            return;
        }

        var children = cell.ToList();
        c.Column(col =>
        {
            col.Spacing(3);
            Blocks(col, children, ctx, 0);
        });
    }

    internal static bool IsNumericColumn(IReadOnlyList<string> values)
    {
        var meaningful = values.Select(v => v.Trim()).Where(v => !Placeholders.Contains(v)).ToList();
        return meaningful.Count > 0 && meaningful.All(v => NumericCell.IsMatch(v));
    }

    internal static string CellText(MdTableCell cell, string source)
    {
        var sb = new StringBuilder();
        foreach (var block in cell)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(block is ParagraphBlock p ? PlainText(p.Inline) : SourceOf(block, source).Trim());
        }
        return sb.ToString();
    }

    // ---------------------------------------------------------------------------------------------
    // Inlines
    // ---------------------------------------------------------------------------------------------

    private static void Inlines(TextDescriptor text, ContainerInline? container, InlineStyle style, Context ctx)
    {
        if (container == null) return;

        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    string content = literal.Content.ToString();
                    Styled(text.Span(ctx.CamelCaseBreaks ? CamelCaseBreak.Replace(content, "$1\u200B$2") : content), style);
                    break;
                case CodeInline code:
                    var codeSpan = BenchmarkPdfStyle.MonoSpan(text.Span(code.Content));
                    codeSpan.BackgroundColor(BenchmarkPdfStyle.InlineCodeBackground);
                    Styled(codeSpan, style);
                    break;
                case LineBreakInline lineBreak:
                    text.Span(lineBreak.IsHard ? "\n" : " ");
                    break;
                case HtmlEntityInline entity:
                    Styled(text.Span(entity.Transcoded.ToString()), style);
                    break;
                case HtmlInline html:
                    Styled(text.Span(html.Tag ?? string.Empty), style);
                    break;
                case AutolinkInline autolink:
                    Link(text, autolink.Url, autolink.IsEmail ? "mailto:" + autolink.Url : autolink.Url, style);
                    break;
                case LinkInline image when image.IsImage:
                    string alt = PlainText(image);
                    Styled(text.Span("[" + (alt.Length > 0 ? alt : "image") + "]"), style);
                    break;
                case LinkInline link:
                    string label = PlainText(link);
                    Link(text, label.Length > 0 ? label : link.Url ?? string.Empty, link.Url, style);
                    break;
                case EmphasisInline emphasis:
                    Inlines(text, emphasis, Emphasis(style, emphasis), ctx);
                    break;
                case ContainerInline other:
                    Inlines(text, other, style, ctx);
                    break;
                default:
                    string raw = SourceOf(inline.Span, ctx.Document.Source);
                    if (raw.Length > 0) Styled(text.Span(raw), style);
                    break;
            }
        }
    }

    internal static InlineStyle Emphasis(InlineStyle style, EmphasisInline emphasis) => emphasis.DelimiterChar switch
    {
        '*' or '_' => emphasis.DelimiterCount >= 2 ? style with { Bold = true } : style with { Italic = true },
        '~' => style with { Strike = true },
        '+' => style with { Underline = true },
        '=' => style with { Marked = true },
        _ => style
    };

    private static void Styled(TextSpanDescriptor span, InlineStyle style)
    {
        if (style.Bold) span.Bold();
        if (style.Italic) span.Italic();
        if (style.Strike) span.Strikethrough();
        if (style.Underline) span.Underline();
        if (style.Marked) span.BackgroundColor(BenchmarkPdfStyle.MarkedBackground);
    }

    /// <summary>A clickable, underlined teal link for an absolute http, https or mailto URL; plain text otherwise.</summary>
    private static void Link(TextDescriptor text, string label, string? url, InlineStyle style)
    {
        if (!IsSafeUrl(url))
        {
            Styled(text.Span(label), style);
            return;
        }

        var span = text.Hyperlink(label, url!);
        Styled(span, style);
        span.FontColor(BenchmarkPdfStyle.Teal).Underline();
    }

    internal static bool IsSafeUrl(string? url)
        => !string.IsNullOrWhiteSpace(url)
            && Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeMailto);

    private static void Align(TextDescriptor text, CellAlign align)
    {
        switch (align)
        {
            case CellAlign.Right:
                text.AlignRight();
                break;
            case CellAlign.Center:
                text.AlignCenter();
                break;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Text helpers
    // ---------------------------------------------------------------------------------------------

    /// <summary>The inline content as plain text: link and emphasis text kept, line breaks as spaces.</summary>
    internal static string PlainText(ContainerInline? container)
    {
        if (container == null) return string.Empty;
        var sb = new StringBuilder();
        AppendPlain(sb, container);
        return sb.ToString().Trim();
    }

    private static void AppendPlain(StringBuilder sb, ContainerInline container)
    {
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    sb.Append(literal.Content.ToString());
                    break;
                case CodeInline code:
                    sb.Append(code.Content);
                    break;
                case LineBreakInline:
                    sb.Append(' ');
                    break;
                case HtmlEntityInline entity:
                    sb.Append(entity.Transcoded.ToString());
                    break;
                case HtmlInline html:
                    sb.Append(html.Tag);
                    break;
                case AutolinkInline autolink:
                    sb.Append(autolink.Url);
                    break;
                case ContainerInline child:
                    AppendPlain(sb, child);
                    break;
            }
        }
    }

    internal static string SourceOf(MarkdownObject node, string source) => SourceOf(node.Span, source);

    internal static string SourceOf(SourceSpan span, string source)
    {
        if (span.IsEmpty || span.Start < 0 || span.End >= source.Length || span.End < span.Start) return string.Empty;
        return source.Substring(span.Start, span.Length);
    }
}
