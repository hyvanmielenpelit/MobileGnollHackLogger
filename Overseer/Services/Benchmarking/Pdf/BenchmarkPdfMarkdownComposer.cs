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
/// otherwise. Any other <c>[[…]]</c> text prints literally. A chart layout
/// (<see cref="BenchmarkReportChartLayout"/>) sets each figure's share of the column width, pairs
/// consecutive figures of one row group side by side, and sets the height cap.</para>
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

    /// <summary>The largest share of the page's content height a figure's image may take without a layout.</summary>
    internal const double FigureMaxHeightShare = BenchmarkReportChartLayout.DefaultMaxHeightShare;

    /// <summary>The space between the two figures of a row, in points.</summary>
    internal const float FigureRowGap = 12f;

    // A lowercase letter or digit followed by an uppercase letter: where a zero-width space lets a
    // header such as ResultLengthChars wrap between its words.
    private static readonly Regex CamelCaseBreak = new(@"(\p{Ll}|\d)(\p{Lu})", RegexOptions.CultureInvariant);

    // Table column sizing, in estimated characters of the 9.5 pt cell text: the A4 text width
    // (170 mm), a cell's horizontal padding, and an average glyph advance of a little over half an em,
    // which scales with the table text size.
    private const double TableWidthPoints = 482;
    private const double CellPaddingPoints = 8;
    private const double AverageCharacterPoints = 5.2;
    private const double MonoWidthScale = 1.25;
    private const double SemiboldWidthScale = 1.08;
    private const int MaxColumnWordCharacters = 24;
    private const int MaxColumnTextCharacters = 60;

    // A character's width in average glyph advances, each class at or above its widest Source Sans 3
    // glyph: the narrow glyphs (i, l, t, r, f, punctuation, spaces), the round capitals, the widest
    // glyphs (m, w, M, W, %, @, the em dash), a full-width CJK glyph, and 1 for every other character.
    private const double NarrowCharacter = 0.65;
    private const double RoundCapitalCharacter = 1.25;
    private const double WideCharacter = 1.55;
    private const double FullWidthCharacter = 1.85;
    private const string NarrowCharacters = " \u00A0il!|'.,:;()[]{}-/\\ftrIj\u2018\u2019\u00B7";
    private const string RoundCapitals = "BCDGHKNOPQRU&";
    private const string WideCharacters = "mwMW%@—";

    /// <summary>The smallest table text size a wide table steps down to, in points, half a point at a time.</summary>
    internal const float MinTableCellSize = 8f;
    private const float TableCellSizeStep = 0.5f;

    // A table of at most this many columns whose preferred widths fill less than this share of the
    // text width is set at those widths, against the left margin, instead of across the page.
    private const int NarrowTableMaxColumns = 3;
    private const double NarrowTableMaxShare = 0.6;

    /// <summary>A table with at most this many body rows is kept on one page when it fits on one.</summary>
    internal const int ShortTableMaxBodyRows = 10;

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

        /// <summary>The first figure paragraph of each row of figures printed side by side, with the row's figures in order.</summary>
        public IReadOnlyDictionary<ParagraphBlock, IReadOnlyList<Figure>> FigureRows { get; init; } = new Dictionary<ParagraphBlock, IReadOnlyList<Figure>>();

        /// <summary>The figure paragraphs a row prints after its first, which print nothing on their own.</summary>
        public IReadOnlySet<ParagraphBlock> RowFollowers { get; init; } = new HashSet<ParagraphBlock>();
    }

    /// <summary>
    /// A chart drawn at a figure marker, numbered from 1 in order of appearance, with its share of the
    /// column width and its row group from the layout.
    /// </summary>
    internal sealed record Figure(int Number, BenchmarkReportRenderChart Chart, double WidthShare = 1, int? RowGroup = null);

    /// <summary>The layout's height cap when it is within its bounds; <see cref="FigureMaxHeightShare"/> otherwise.</summary>
    internal static double MaxHeightShareOf(BenchmarkReportChartLayout? layout)
        => layout?.MaxHeightShare is double share
           && share >= BenchmarkReportChartLayout.MinMaxHeightShare && share <= BenchmarkReportChartLayout.MaxMaxHeightShare
            ? share
            : FigureMaxHeightShare;

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
    /// Each top-level figure marker whose key has a chart in <paramref name="charts"/> becomes a figure,
    /// with its width share and row group from <paramref name="layout"/>; two figures of one row group
    /// with nothing printed between them form a row.
    /// </summary>
    public static Prepared Prepare(
        string markdown, string title, IReadOnlyList<BenchmarkReportRenderChart>? charts = null, BenchmarkReportChartLayout? layout = null)
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

            var placements = new Dictionary<string, BenchmarkReportChartLayoutFigure>(StringComparer.Ordinal);
            foreach (var placement in layout?.Figures ?? new List<BenchmarkReportChartLayoutFigure>())
            {
                if (placement != null && !string.IsNullOrEmpty(placement.Key)) placements.TryAdd(placement.Key, placement);
            }

            foreach (var paragraph in blocks.OfType<ParagraphBlock>())
            {
                if (FigureKeyOf(paragraph, source) is string key && byKey.TryGetValue(key, out var chart))
                {
                    placements.TryGetValue(key, out var placement);
                    double share = placement != null && placement.WidthShare > 0 && placement.WidthShare <= 1 ? placement.WidthShare : 1;
                    figures[paragraph] = new Figure(figures.Count + 1, chart, share, placement?.RowGroup);
                }
            }
        }

        var (rows, followers) = FigureRowsOf(blocks, figures, source);
        return new Prepared
        {
            Source = source,
            Blocks = blocks,
            SectionIds = sectionIds,
            Contents = contents,
            Figures = figures,
            FigureRows = rows,
            RowFollowers = followers
        };
    }

    /// <summary>
    /// The rows of figures printed side by side: a figure with a row group and the next block that
    /// prints something, when that is a figure of the same row group, two to a row.
    /// </summary>
    private static (Dictionary<ParagraphBlock, IReadOnlyList<Figure>> Rows, HashSet<ParagraphBlock> Followers) FigureRowsOf(
        IReadOnlyList<Block> blocks, IReadOnlyDictionary<ParagraphBlock, Figure> figures, string source)
    {
        var rows = new Dictionary<ParagraphBlock, IReadOnlyList<Figure>>();
        var followers = new HashSet<ParagraphBlock>();

        bool PrintsNothing(Block block)
            => block is LinkReferenceDefinitionGroup
               || (block is ParagraphBlock marker && !figures.ContainsKey(marker) && FigureKeyOf(marker, source) != null);

        for (int i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] is not ParagraphBlock first || !figures.TryGetValue(first, out var leader) || leader.RowGroup is not int group)
            {
                continue;
            }

            int next = i + 1;
            while (next < blocks.Count && PrintsNothing(blocks[next])) next++;
            if (next < blocks.Count && blocks[next] is ParagraphBlock second
                && figures.TryGetValue(second, out var partner) && partner.RowGroup == group)
            {
                rows[first] = new[] { leader, partner };
                followers.Add(second);
                i = next;
            }
        }

        return (rows, followers);
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
    /// drawn figure (a row of figures by its first) at its image's height cap and a caption, a table at two lines of cell text per
    /// source line, and any other block at one line of body text per <see cref="EstimatedLineCharacters"/>
    /// characters of each source line. It errs high, so a section it keeps together fits.
    /// </summary>
    internal static float EstimatedHeight(Block block, Prepared document, FigureFrame frame)
    {
        switch (block)
        {
            case HeadingBlock heading:
                return HeadingHeight(heading.Level);
            case ParagraphBlock paragraph when document.RowFollowers.Contains(paragraph):
                return 0;
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
                || (block is ParagraphBlock marker && (IsUndrawnMarker(marker, ctx.Document) || ctx.Document.RowFollowers.Contains(marker))))
            {
                continue;
            }

            if (block is HeadingBlock)
            {
                // A run of consecutive headings keeps with what follows: a first paragraph or figure
                // moves with it when the group does not fit, and before anything else the run needs
                // room on its page for its own height and the first lines of the next block.
                int last = i;
                while (last + 1 < blocks.Count && blocks[last + 1] is HeadingBlock) last++;
                var headings = blocks.Skip(i).Take(last - i + 1).Cast<HeadingBlock>().ToList();
                int? nextIndex = FirstPrintedAfter(blocks, last, ctx.Document);
                var next = nextIndex is int n ? blocks[n] : null;

                if (next is ParagraphBlock paragraph)
                {
                    col.Item().PreventPageBreak().Column(group =>
                    {
                        group.Spacing(BenchmarkPdfStyle.BlockSpacing);
                        foreach (var heading in headings) group.Item().Element(c => Heading(c, heading, ctx));
                        group.Item().Element(c => ParagraphOrFigure(c, paragraph, ctx));
                    });
                    i = nextIndex!.Value;
                }
                else if (next is MdTable shortTable && IsShortTable(shortTable))
                {
                    // A short table is kept on one page, so its headings move with it rather than stay behind.
                    col.Item().PreventPageBreak().Column(group =>
                    {
                        group.Spacing(BenchmarkPdfStyle.BlockSpacing);
                        foreach (var heading in headings) group.Item().Element(c => Heading(c, heading, ctx));
                        group.Item().Element(c => ComposeTable(c, shortTable, ctx));
                    });
                    i = nextIndex!.Value;
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
    /// The index of the block a run of headings ending at <paramref name="last"/> keeps with: the first
    /// later block that prints something, past link reference definitions and figure markers without
    /// a chart; null when none does.
    /// </summary>
    internal static int? FirstPrintedAfter(IReadOnlyList<Block> blocks, int last, Prepared document)
    {
        for (int i = last + 1; i < blocks.Count; i++)
        {
            if (blocks[i] is LinkReferenceDefinitionGroup
                || (blocks[i] is ParagraphBlock marker && IsUndrawnMarker(marker, document)))
            {
                continue;
            }
            return i;
        }
        return null;
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

    /// <summary>
    /// A figure, or a row of figures from its first, when the paragraph is a marker with a chart, else
    /// the paragraph; an undrawn marker prints nothing.
    /// </summary>
    private static void ParagraphOrFigure(IContainer container, ParagraphBlock paragraph, Context ctx)
    {
        if (ctx.Document.FigureRows.TryGetValue(paragraph, out var row))
        {
            ComposeFigureRow(container, row, ctx);
        }
        else if (ctx.Document.Figures.TryGetValue(paragraph, out var figure))
        {
            ComposeFigure(container, figure, ctx);
        }
        else if (!IsUndrawnMarker(paragraph, ctx.Document))
        {
            Paragraph(container, paragraph.Inline, ctx, CellAlign.Left);
        }
    }

    /// <summary>
    /// A tagged figure, centered in a frame of its width share of the text column: the chart image as
    /// wide as the frame unless its height reaches the frame's cap, and below it the caption
    /// "<b>Figure N.</b> <i>Title</i> — caption" in the secondary size, kept on one page with the image.
    /// </summary>
    private static void ComposeFigure(IContainer container, Figure figure, Context ctx)
    {
        float frameWidth = (float)(ctx.Frame.Width * figure.WidthShare);
        var target = container.PaddingVertical(4).PreventPageBreak();
        if (figure.WidthShare < 1)
        {
            target = target.AlignCenter().Width(frameWidth);
        }
        FigureColumn(target, figure, frameWidth, ctx);
    }

    /// <summary>
    /// Two figures side by side, kept on one page: the text column, less <see cref="FigureRowGap"/>,
    /// shared between them by their width shares, each with its own image and caption.
    /// </summary>
    private static void ComposeFigureRow(IContainer container, IReadOnlyList<Figure> figures, Context ctx)
    {
        float total = (float)figures.Sum(f => f.WidthShare);
        float available = ctx.Frame.Width - FigureRowGap * (figures.Count - 1);

        container.PaddingVertical(4).PreventPageBreak().Row(row =>
        {
            row.Spacing(FigureRowGap);
            foreach (var figure in figures)
            {
                float share = (float)figure.WidthShare;
                row.RelativeItem(share).Element(c => FigureColumn(c, figure, available * share / total, ctx));
            }
        });
    }

    /// <summary>The image, centered and sized within <paramref name="maxWidth"/> and the frame's height cap, above its caption.</summary>
    private static void FigureColumn(IContainer container, Figure figure, float maxWidth, Context ctx)
    {
        var chart = figure.Chart;
        var (width, height) = FigureSize(chart.WidthPx, chart.HeightPx, maxWidth, ctx.Frame.MaxHeight);
        var (label, title, caption) = CaptionParts(figure);

        container.Column(col =>
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
    /// start on the same page; a row taller than a page breaks across pages. A table of at most
    /// <see cref="ShortTableMaxBodyRows"/> body rows is kept on one page when it fits on one. A column
    /// whose non-empty body cells are all numbers, percentages, currency amounts or durations is
    /// right-aligned unless the Markdown sets its alignment. The text size, the column widths, the
    /// short headers with their legend line below the table, and a column set on a second line of each
    /// body row are <see cref="TableLayout"/>'s.
    /// </summary>
    /// <summary>A table <see cref="ComposeTable"/> keeps on one page: at most <see cref="ShortTableMaxBodyRows"/> body rows.</summary>
    internal static bool IsShortTable(MdTable table)
        => table.OfType<MdTableRow>().Count(r => !r.IsHeader) <= ShortTableMaxBodyRows;

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

        var layout = TableLayout(table, headerRows, bodyRows, columns, ctx.Document.Source);
        string secondLineLabel = layout.SecondLineColumn is int moved && headerRows.Count > 0 && CellAt(headerRows[0], moved) is { } movedHeader
            ? CellText(movedHeader, ctx.Document.Source)
            : string.Empty;

        void Columns(TableDescriptor t) => t.ColumnsDefinition(cd =>
        {
            foreach (float width in layout.Widths)
            {
                if (layout.Constant) cd.ConstantColumn(width);
                else cd.RelativeColumn(width);
            }
        });

        void Grid(IContainer grid) => grid.SemanticTable().Decoration(d =>
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
                            PlaceRow(() => h.Cell(), headerRows[r], (uint)(r + 1), columns, layout, header: true, zebra: false, ctx);
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
                        PlaceRow(() => t.Cell(), cells, 1, columns, layout, header: false, zebra, ctx);
                        if (layout.SecondLineColumn is int second)
                        {
                            SecondLine(t.Cell().Row(2).Column(1).ColumnSpan((uint)layout.GridColumns.Length),
                                CellAt(cells, second), secondLineLabel, zebra, layout.FontSize, ctx);
                        }
                    });
                }
            });
        });

        var target = layout.Constant ? container.AlignLeft() : container;
        if (bodyRows.Count <= ShortTableMaxBodyRows) target = target.PreventPageBreak();

        if (layout.Legend is not string legend)
        {
            Grid(target);
            return;
        }

        target.Column(col =>
        {
            col.Spacing(3);
            col.Item().Element(Grid);
            col.Item().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(BenchmarkPdfStyle.SmallSize).FontColor(BenchmarkPdfStyle.Muted));
                t.Span(legend);
            });
        });
    }

    /// <summary>
    /// The PDF's column layout of <see cref="TableLayout"/>: each source column's alignment, the grid
    /// columns' widths, and whether those are constant widths in points.
    /// </summary>
    internal static (CellAlign[] Aligns, float[] Widths, bool Constant) PdfColumnLayout(
        MdTable table, IReadOnlyList<List<MdTableCell>> headerRows, IReadOnlyList<List<MdTableCell>> bodyRows,
        int columns, string source)
    {
        var layout = TableLayout(table, headerRows, bodyRows, columns, source);
        return (layout.Aligns, layout.Widths, layout.Constant);
    }

    /// <summary>The table text sizes a wide table tries, largest first: the base size, then down by half a point to <see cref="MinTableCellSize"/>.</summary>
    internal static IReadOnlyList<float> TableFontSizes()
    {
        var sizes = new List<float> { BenchmarkPdfStyle.TableCellSize };
        for (float size = BenchmarkPdfStyle.TableCellSize - TableCellSizeStep; size >= MinTableCellSize - 0.001f; size -= TableCellSizeStep)
        {
            sizes.Add(size);
        }
        return sizes;
    }

    /// <summary>
    /// How a table is set, in the PDF and in Word. A table of at most <see cref="NarrowTableMaxColumns"/>
    /// columns whose preferred widths fill less than <see cref="NarrowTableMaxShare"/> of the text width
    /// is set at those widths in points. Any other table spans the text width, and when its columns'
    /// longest words (<see cref="MeasureColumns"/>) do not fit across it, these measures are taken in
    /// order until they do: the text size steps down through <see cref="TableFontSizes"/>; then the
    /// headers of <see cref="BenchmarkTableLayout.HeaderAbbreviations"/> that shorten their column's
    /// longest word are printed short, one at a time, the largest saving first; then a
    /// <see cref="BenchmarkTableLayout.SecondLineHeader"/> column moves onto a full-width second line of
    /// each body row, with as few short headers as then fit. Each measure is tried at every text size
    /// before the next is taken, and the largest size that fits wins. When nothing fits, every measure
    /// is taken at the smallest size and <see cref="PdfColumnWeights"/> shares out the shortfall.
    /// </summary>
    internal static BenchmarkTableLayout TableLayout(
        MdTable table, IReadOnlyList<List<MdTableCell>> headerRows, IReadOnlyList<List<MdTableCell>> bodyRows,
        int columns, string source)
    {
        var measure = MeasureColumns(table, headerRows, bodyRows, columns, source);
        var all = Enumerable.Range(0, columns).ToArray();
        float baseSize = BenchmarkPdfStyle.TableCellSize;

        double[] MinimumOf(double[] headerMinimum)
            => all.Select(c => Math.Max(measure.BodyMinimum[c], headerMinimum[c])).ToArray();
        double[] PreferredOf(double[] minimum, double[] headerLongest)
            => all.Select(c => Math.Max(minimum[c], Math.Min(Math.Max(measure.BodyLongest[c], headerLongest[c]), MaxColumnTextCharacters))).ToArray();

        var fullMinimum = MinimumOf(measure.HeaderMinimum);
        var fullPreferred = PreferredOf(fullMinimum, measure.HeaderLongest);
        var preferredPoints = fullPreferred.Select(w => Points(w, baseSize)).ToArray();
        if (columns <= NarrowTableMaxColumns && preferredPoints.Sum() < TableWidthPoints * NarrowTableMaxShare)
        {
            return new BenchmarkTableLayout
            {
                Aligns = measure.Aligns,
                GridColumns = all,
                Widths = preferredPoints,
                Weights = ColumnWeights(measure.BodyMinimum, fullMinimum, fullPreferred),
                Constant = true,
                FontSize = baseSize
            };
        }

        // The short headers that shorten their column's longest word, the largest saving first.
        var abbreviations = new List<(BenchmarkTableAbbreviation Abbreviation, double HeaderMinimum, double Saving)>();
        for (int c = 0; c < columns; c++)
        {
            if (measure.HeaderText[c] is string header
                && BenchmarkTableLayout.HeaderAbbreviations.TryGetValue(header, out string? shortHeader))
            {
                double headerMinimum = HeaderMinimum(shortHeader);
                double saving = fullMinimum[c] - Math.Max(measure.BodyMinimum[c], headerMinimum);
                if (saving > 0) abbreviations.Add((new BenchmarkTableAbbreviation(c, shortHeader, header), headerMinimum, saving));
            }
        }
        abbreviations = abbreviations.OrderByDescending(a => a.Saving).ThenBy(a => a.Abbreviation.Column).ToList();

        int topicIndex = Array.FindIndex(measure.HeaderText, h => h == BenchmarkTableLayout.SecondLineHeader);
        int? topic = columns > 1 && topicIndex >= 0 && measure.Aligns[topicIndex] != CellAlign.Right ? topicIndex : null;

        var levels = new List<(int Abbreviated, int? Moved)>();
        for (int k = 0; k <= abbreviations.Count; k++) levels.Add((k, null));
        if (topic != null)
        {
            for (int k = 0; k <= abbreviations.Count; k++) levels.Add((k, topic));
        }

        BenchmarkTableLayout? Try((int Abbreviated, int? Moved) level, float? only)
        {
            var used = abbreviations.Take(level.Abbreviated).ToList();
            var headerMinimum = (double[])measure.HeaderMinimum.Clone();
            var headerLongest = (double[])measure.HeaderLongest.Clone();
            foreach (var (abbreviation, shortMinimum, _) in used)
            {
                headerMinimum[abbreviation.Column] = shortMinimum;
                headerLongest[abbreviation.Column] = abbreviation.Short.Length;
            }
            var minimum = MinimumOf(headerMinimum);
            var preferred = PreferredOf(minimum, headerLongest);
            var grid = all.Where(c => c != level.Moved).ToArray();
            double needed = grid.Sum(c => minimum[c]);

            foreach (float size in only is float forced ? new[] { forced } : TableFontSizes())
            {
                bool fits = needed <= Capacity(grid.Length, size);
                if (!fits && only == null) continue;

                T[] Pick<T>(T[] values) => grid.Select(c => values[c]).ToArray();
                var widths = PdfColumnWeights(Pick(measure.Aligns), Pick(measure.BodyMinimum), Pick(headerMinimum), Pick(minimum), Pick(preferred), size);
                return new BenchmarkTableLayout
                {
                    Aligns = measure.Aligns,
                    GridColumns = grid,
                    Widths = widths,
                    Weights = widths,
                    FontSize = size,
                    Abbreviations = used.Select(u => u.Abbreviation).OrderBy(a => a.Column).ToList(),
                    SecondLineColumn = level.Moved,
                    Fits = fits
                };
            }
            return null;
        }

        foreach (var level in levels)
        {
            if (Try(level, null) is { } layout) return layout;
        }
        return Try(levels[^1], MinTableCellSize)!;
    }

    /// <summary>
    /// Relative column widths for the PDF at the table text size <paramref name="fontSize"/>, in points
    /// for the A4 text width. Every column's minimum is its longest token, header and body together.
    /// When the minimums fit, the widths are those of <see cref="ColumnWeights"/>. When they do not, a
    /// right-aligned (numeric) column keeps its minimum and only the other columns shrink, each in
    /// proportion to how far its minimum exceeds its header's longest token, so a header word does not
    /// break inside itself; only when the header tokens alone do not fit do those columns shrink in
    /// proportion to them.
    /// </summary>
    internal static float[] PdfColumnWeights(
        CellAlign[] aligns, double[] bodyMinimum, double[] headerMinimum, double[] minimum, double[] preferred,
        float fontSize = BenchmarkPdfStyle.TableCellSize)
    {
        int columns = minimum.Length;
        double capacity = Capacity(columns, fontSize);
        if (minimum.Sum() <= capacity) return ColumnWeights(bodyMinimum, minimum, preferred, fontSize);

        var text = Enumerable.Range(0, columns).Where(c => aligns[c] != CellAlign.Right).ToList();
        var widths = (double[])minimum.Clone();
        double room = capacity - Enumerable.Range(0, columns).Where(c => aligns[c] == CellAlign.Right).Sum(c => minimum[c]);

        if (text.Count > 0 && room > 0)
        {
            var floor = text.ToDictionary(c => c, c => Math.Min(headerMinimum[c], minimum[c]));
            double floorSum = floor.Values.Sum();
            double textSum = text.Sum(c => minimum[c]);

            foreach (int c in text)
            {
                widths[c] = room >= floorSum
                    ? floor[c] + (minimum[c] - floor[c]) * (room - floorSum) / (textSum - floorSum)
                    : floor[c] * room / floorSum;
            }
        }

        return widths.Select(w => Points(w, fontSize)).ToArray();
    }

    /// <summary>The width in points of a column of <paramref name="characters"/> estimated characters at the table text size, with its cell padding.</summary>
    private static float Points(double characters, float fontSize) => (float)(characters * CharacterPoints(fontSize) + CellPaddingPoints);

    /// <summary>The average glyph advance of the cell text at <paramref name="fontSize"/>, in points.</summary>
    private static double CharacterPoints(float fontSize) => AverageCharacterPoints * fontSize / BenchmarkPdfStyle.TableCellSize;

    /// <summary>The estimated characters of cell text at <paramref name="fontSize"/> a table of <paramref name="columns"/> columns holds across the text width.</summary>
    private static double Capacity(int columns, float fontSize)
        => Math.Max(columns * 3.0, (TableWidthPoints - columns * CellPaddingPoints) / CharacterPoints(fontSize));

    /// <summary>
    /// A table's columns in estimated characters of the regular cell text at the base table size: each
    /// column's alignment (a declared alignment, else right for a numeric column and left otherwise), its
    /// body's and its header's longest word, its longest body text and header text, and, when the table
    /// has exactly one header row, that header's plain text.
    /// </summary>
    private sealed record ColumnMeasure(
        CellAlign[] Aligns, double[] BodyMinimum, double[] HeaderMinimum, double[] BodyLongest, double[] HeaderLongest,
        string?[] HeaderText);

    private static ColumnMeasure MeasureColumns(
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
        var headerMinimum = new double[columns];
        var bodyLongest = new double[columns];
        var headerLongest = new double[columns];
        var headerText = new string?[columns];
        for (int c = 0; c < columns; c++)
        {
            var bodyCells = bodyRows.Select(cells => CellAt(cells, c)).Where(cell => cell != null).Select(cell => cell!).ToList();
            var headerCells = headerRows.Select(cells => CellAt(cells, c)).Where(cell => cell != null).Select(cell => cell!).ToList();
            var bodyValues = bodyCells.Select(TextOf).ToList();

            MdTableColumnAlign? declared = c < table.ColumnDefinitions.Count ? table.ColumnDefinitions[c].Alignment : null;
            aligns[c] = declared switch
            {
                MdTableColumnAlign.Right => CellAlign.Right,
                MdTableColumnAlign.Center => CellAlign.Center,
                MdTableColumnAlign.Left => CellAlign.Left,
                _ => IsNumericColumn(bodyValues) ? CellAlign.Right : CellAlign.Left
            };

            // Monospace code is measured by its length, scaled; any other body text by its glyphs.
            bodyMinimum[c] = Fit(bodyCells
                .SelectMany(cell => cell.Descendants<CodeInline>().Any()
                    ? Words(TextOf(cell)).Select(w => w.Length * MonoWidthScale)
                    : Words(TextOf(cell)).Select(WordCharacters))
                .DefaultIfEmpty(1)
                .Max());
            headerMinimum[c] = headerCells.Select(cell => HeaderMinimum(TextOf(cell))).DefaultIfEmpty(Fit(1)).Max();
            bodyLongest[c] = bodyValues.Select(v => v.Length).DefaultIfEmpty(1).Max();
            headerLongest[c] = headerCells.Select(cell => TextOf(cell).Length).DefaultIfEmpty(0).Max();
            if (headerRows.Count == 1 && headerCells.Count == 1 && Math.Max(1, headerCells[0].ColumnSpan) == 1)
            {
                headerText[c] = TextOf(headerCells[0]).Trim();
            }
        }

        return new ColumnMeasure(aligns, bodyMinimum, headerMinimum, bodyLongest, headerLongest, headerText);
    }

    /// <summary>
    /// A header's longest word in estimated characters, with its slack: the semibold header is wider,
    /// and its identifiers may wrap at their CamelCase boundaries.
    /// </summary>
    private static double HeaderMinimum(string header)
        => Fit(Words(CamelCaseBreak.Replace(header, "$1 $2")).Select(w => WordCharacters(w) * SemiboldWidthScale).DefaultIfEmpty(1).Max());

    /// <summary>A longest word's column minimum: its estimated characters rounded up, one more for slack, between 3 and <see cref="MaxColumnWordCharacters"/>.</summary>
    private static double Fit(double characters) => Math.Clamp(Math.Ceiling(characters) + 1, 3, MaxColumnWordCharacters);

    /// <summary>
    /// The pieces a line may not break inside: the text split at every breaking space and zero-width
    /// space, and after a hyphen followed by a letter. A no-break space joins its neighbors.
    /// </summary>
    internal static IEnumerable<string> Words(string text)
    {
        var word = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\u200B' || (char.IsWhiteSpace(c) && c is not ('\u00A0' or '\u2007' or '\u202F')))
            {
                if (word.Length > 0) yield return word.ToString();
                word.Clear();
                continue;
            }
            word.Append(c);
            if (c == '-' && i + 1 < text.Length && char.IsLetter(text[i + 1]))
            {
                yield return word.ToString();
                word.Clear();
            }
        }
        if (word.Length > 0) yield return word.ToString();
    }

    /// <summary>A word's width in estimated characters of the regular cell text, by the width class of each of its characters.</summary>
    internal static double WordCharacters(string word)
    {
        double width = 0;
        foreach (char c in word)
        {
            width += NarrowCharacters.Contains(c) ? NarrowCharacter
                : WideCharacters.Contains(c) ? WideCharacter
                : RoundCapitals.Contains(c) ? RoundCapitalCharacter
                : c >= '\u2E80' && !char.IsSurrogate(c) ? FullWidthCharacter
                : 1.0;
        }
        return width;
    }

    /// <summary>
    /// Relative column widths in the manner of an automatic HTML table layout, in estimated characters.
    /// Every column gets its longest word, header words included, when the page can hold them all, and
    /// the width left over is shared in proportion to how much longer each column's longest text is.
    /// When it cannot, the body's longest words come first and the header's share what remains, so a
    /// value never breaks inside a word before a column heading does. When even the body's words do not
    /// fit, the columns are sized by them, so words break as evenly as they can.
    /// </summary>
    internal static float[] ColumnWeights(
        double[] bodyMinimum, double[] minimum, double[] preferred, float fontSize = BenchmarkPdfStyle.TableCellSize)
    {
        int columns = minimum.Length;
        double capacity = Capacity(columns, fontSize);

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
        return widths.Select(w => Points(w, fontSize)).ToArray();
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

    /// <summary>
    /// One row of cells placed in the layout's grid: a cell of the second-line column is left out, a
    /// header the layout prints short prints short, and a short row gets empty cells.
    /// </summary>
    private static void PlaceRow(
        Func<ITableCellContainer> newCell, List<MdTableCell> cells, uint row, int columns, BenchmarkTableLayout layout,
        bool header, bool zebra, Context ctx)
    {
        int position = 0;
        foreach (var cell in cells)
        {
            if (position >= columns) break;
            int span = Math.Clamp(cell.ColumnSpan, 1, columns - position);
            var (first, gridSpan) = layout.GridSpanOf(position, span);
            if (gridSpan > 0)
            {
                var slot = newCell().Row(row).Column((uint)first + 1);
                if (gridSpan > 1) slot = slot.ColumnSpan((uint)gridSpan);
                string? shortHeader = header && span == 1 ? layout.HeaderTextOf(position) : null;
                CellBody(slot, cell, layout.Aligns[position], header, zebra, layout.FontSize, ctx, shortHeader);
            }
            position += span;
        }

        // A short row gets empty cells, so every row carries the full set of rules.
        for (; position < columns; position++)
        {
            var (first, gridSpan) = layout.GridSpanOf(position, 1);
            if (gridSpan > 0)
            {
                CellBody(newCell().Row(row).Column((uint)first + 1), null, layout.Aligns[position], header, zebra, layout.FontSize, ctx);
            }
        }
    }

    private static void CellBody(
        IContainer slot, MdTableCell? cell, CellAlign align, bool header, bool zebra, float fontSize, Context ctx,
        string? shortHeader = null)
    {
        IContainer c = slot;
        if (header) c = c.Background(BenchmarkPdfStyle.TableHeader);
        else if (zebra) c = c.Background(BenchmarkPdfStyle.Zebra);

        c = c.Border(BenchmarkPdfStyle.Hairline, BenchmarkPdfStyle.Rule)
            .PaddingVertical(2.5f).PaddingHorizontal(4)
            .DefaultTextStyle(s => header
                ? BenchmarkPdfStyle.Semibold(s).FontSize(fontSize).LineHeight(1.25f)
                : s.FontSize(fontSize).LineHeight(1.25f));

        if (shortHeader != null)
        {
            c.Text(t =>
            {
                Align(t, align);
                t.Span(shortHeader);
            });
            return;
        }

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

    /// <summary>
    /// The second line of a body row: the moved column's cell across the whole row, after its header
    /// in semibold, as "Topic: …", on the row's stripe.
    /// </summary>
    private static void SecondLine(IContainer slot, MdTableCell? cell, string label, bool zebra, float fontSize, Context ctx)
    {
        IContainer c = zebra ? slot.Background(BenchmarkPdfStyle.Zebra) : slot;
        c.Border(BenchmarkPdfStyle.Hairline, BenchmarkPdfStyle.Rule)
            .PaddingVertical(2.5f).PaddingHorizontal(4)
            .DefaultTextStyle(s => s.FontSize(fontSize).LineHeight(1.25f))
            .Text(t =>
            {
                if (label.Length > 0) BenchmarkPdfStyle.SemiboldSpan(t.Span(label + ": "));
                if (cell is { Count: 1 } && cell[0] is ParagraphBlock paragraph)
                {
                    Inlines(t, paragraph.Inline, default, ctx);
                }
                else if (cell != null)
                {
                    t.Span(CellText(cell, ctx.Document.Source));
                }
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
