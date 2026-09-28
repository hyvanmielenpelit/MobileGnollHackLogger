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
        "", "—", "–", "-", "N/A", "NA", "n.a."
    };

    private static readonly string[] Bullets = { "•", "◦", "▪" };

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

    private enum CellAlign
    {
        Left,
        Center,
        Right
    }

    private readonly record struct InlineStyle(bool Bold, bool Italic, bool Strike, bool Underline, bool Marked);

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
    }

    /// <summary><paramref name="CamelCaseBreaks"/>: a table header, whose identifiers may wrap between words.</summary>
    private sealed record Context(Prepared Document, CancellationToken Token, bool CamelCaseBreaks = false);

    public static MarkdownDocument Parse(string markdown) => Markdown.Parse(markdown ?? string.Empty, Pipeline);

    /// <summary>Parses <paramref name="markdown"/> and drops its first <c>#</c> heading when it repeats <paramref name="title"/>.</summary>
    public static Prepared Prepare(string markdown, string title)
    {
        var document = Parse(markdown);
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

        return new Prepared
        {
            Source = markdown ?? string.Empty,
            Blocks = blocks,
            SectionIds = sectionIds,
            Contents = contents
        };
    }

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

    /// <summary>The body. The token is checked between blocks, so a canceled request stops composing.</summary>
    public static void ComposeBody(IContainer container, Prepared document, CancellationToken cancellationToken)
    {
        var ctx = new Context(document, cancellationToken);
        container.Column(col =>
        {
            col.Spacing(BenchmarkPdfStyle.BlockSpacing);
            Blocks(col, document.Blocks, ctx, 0);
        });
    }

    // ---------------------------------------------------------------------------------------------
    // Blocks
    // ---------------------------------------------------------------------------------------------

    private static void Blocks(ColumnDescriptor col, IReadOnlyList<Block> blocks, Context ctx, int listDepth)
    {
        for (int i = 0; i < blocks.Count; i++)
        {
            ctx.Token.ThrowIfCancellationRequested();
            var block = blocks[i];

            if (block is LinkReferenceDefinitionGroup)
            {
                continue;
            }

            if (block is HeadingBlock heading)
            {
                // A heading keeps with what follows: a first paragraph moves with it when the pair
                // does not fit, and before anything else it needs room below it on its page.
                if (i + 1 < blocks.Count && blocks[i + 1] is ParagraphBlock paragraph)
                {
                    col.Item().PreventPageBreak().Column(pair =>
                    {
                        pair.Spacing(BenchmarkPdfStyle.BlockSpacing);
                        pair.Item().Element(c => Heading(c, heading, ctx));
                        pair.Item().Element(c => Paragraph(c, paragraph.Inline, ctx, CellAlign.Left));
                    });
                    i++;
                }
                else
                {
                    col.Item().EnsureSpace(BenchmarkPdfStyle.KeepWithNextHeight).Element(c => Heading(c, heading, ctx));
                }
                continue;
            }

            col.Item().Element(c => ComposeBlock(c, block, ctx, listDepth));
        }
    }

    private static void ComposeBlock(IContainer container, Block block, Context ctx, int listDepth)
    {
        switch (block)
        {
            case HeadingBlock heading:
                Heading(container, heading, ctx);
                break;
            case ParagraphBlock paragraph:
                Paragraph(container, paragraph.Inline, ctx, CellAlign.Left);
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
    /// A pipe table: header row repeated on every page, zebra body rows, hairline rules, and a row
    /// kept whole on one page when it fits on one. A column whose non-empty body cells are all
    /// numbers, percentages, currency amounts or durations is right-aligned unless the Markdown
    /// sets its alignment.
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

        var texts = new Dictionary<MdTableCell, string>();
        string TextOf(MdTableCell cell)
        {
            if (!texts.TryGetValue(cell, out string? value))
            {
                value = CellText(cell, ctx.Document.Source);
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
        var weights = ColumnWeights(bodyMinimum, minimum, preferred);

        container.SemanticTable().Table(t =>
        {
            t.ColumnsDefinition(cd =>
            {
                foreach (float weight in weights) cd.RelativeColumn(weight);
            });

            if (headerRows.Count > 0)
            {
                t.Header(h =>
                {
                    for (int r = 0; r < headerRows.Count; r++)
                    {
                        PlaceRow(() => h.Cell(), headerRows[r], (uint)(r + 1), columns, aligns, header: true, zebra: false, ctx);
                    }
                });
            }

            for (int r = 0; r < bodyRows.Count; r++)
            {
                ctx.Token.ThrowIfCancellationRequested();
                PlaceRow(() => t.Cell(), bodyRows[r], (uint)(r + 1), columns, aligns, header: false, zebra: r % 2 == 1, ctx);
            }
        });
    }

    /// <summary>
    /// Relative column widths in the manner of an automatic HTML table layout, in estimated characters.
    /// Every column gets its longest word, header words included, when the page can hold them all, and
    /// the width left over is shared in proportion to how much longer each column's longest text is.
    /// When it cannot, the body's longest words come first and the header's share what remains, so a
    /// value never breaks inside a word before a column heading does. When even the body's words do not
    /// fit, the columns are sized by them, so words break as evenly as they can.
    /// </summary>
    private static float[] ColumnWeights(double[] bodyMinimum, double[] minimum, double[] preferred)
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

    private static MdTableCell? CellAt(List<MdTableCell> cells, int column)
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
        IContainer c = header ? slot : slot.PreventPageBreak();
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

    private static bool IsNumericColumn(IReadOnlyList<string> values)
    {
        var meaningful = values.Select(v => v.Trim()).Where(v => !Placeholders.Contains(v)).ToList();
        return meaningful.Count > 0 && meaningful.All(v => NumericCell.IsMatch(v));
    }

    private static string CellText(MdTableCell cell, string source)
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

    private static InlineStyle Emphasis(InlineStyle style, EmphasisInline emphasis) => emphasis.DelimiterChar switch
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

    private static bool IsSafeUrl(string? url)
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

    private static string SourceOf(MarkdownObject node, string source) => SourceOf(node.Span, source);

    private static string SourceOf(SourceSpan span, string source)
    {
        if (span.IsEmpty || span.Start < 0 || span.End >= source.Length || span.End < span.Start) return string.Empty;
        return source.Substring(span.Start, span.Length);
    }
}
