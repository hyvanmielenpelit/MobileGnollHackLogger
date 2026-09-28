namespace Overseer.Services.Benchmarking.Word;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using Overseer.Services.Benchmarking.Pdf;
using Palette = Overseer.Services.Benchmarking.Pdf.BenchmarkDocumentPalette;

/// <summary>One ordered Markdown list: its own <c>w:num</c>, the level it sits at, its start and its delimiter.</summary>
internal sealed record BenchmarkWordOrderedList(int NumberingId, int Level, int Start, char Delimiter);

/// <summary>
/// The Word documents' styles and list numbering. Every piece of formatting is a named style, so the
/// file edits like one Word made: built-in styles carry Word's own ids and names (<c>Heading1</c>,
/// "heading 1"), which the Styles pane, the Navigation pane, the table of contents and the
/// accessibility checker recognize; the rest are custom styles named for what they format. The
/// base text formatting sits in the document defaults, as in Word's own templates, so a table style's
/// text size applies inside its table.
/// </summary>
internal static class BenchmarkWordStyles
{
    public const string Normal = "Normal";
    public const string Title = "Title";
    public const string Subtitle = "Subtitle";
    public const string DocumentKind = "DocumentKind";
    public const string TocHeading = "TOCHeading";
    public const string Toc2 = "TOC2";
    public const string ListParagraph = "ListParagraph";
    public const string Quote = "Quote";
    public const string CodeBlock = "CodeBlock";
    public const string HorizontalRule = "HorizontalRule";
    public const string ContentsRule = "ContentsRule";
    public const string SourceLine = "SourceLine";
    public const string ClassificationInternal = "ClassificationInternal";
    public const string ClassificationProvider = "ClassificationProvider";
    public const string Header = "Header";
    public const string Footer = "Footer";
    public const string Hyperlink = "Hyperlink";
    public const string InlineCode = "InlineCode";
    public const string DefaultParagraphFont = "DefaultParagraphFont";
    public const string TableNormal = "TableNormal";
    public const string NoList = "NoList";
    public const string TableGrid = "TableGrid";
    public const string GnollBenchTable = "GnollBenchTable";
    public const string GnollBenchFacts = "GnollBenchFacts";

    /// <summary>The numbering id every bullet list shares; ordered lists are numbered from the next one up.</summary>
    public const int BulletNumberingId = 1;

    public const int FirstOrderedNumberingId = 2;

    private const int BulletAbstractId = 0;
    private const int PeriodAbstractId = 1;
    private const int ParenthesisAbstractId = 2;

    /// <summary>Bullets by list level, cycling as the PDF's do.</summary>
    private static readonly string[] Bullets = { "•", "◦", "▪" };

    public static string Heading(int level) => "Heading" + Math.Clamp(level, 1, 6).ToString(CultureInfo.InvariantCulture);

    /// <summary>A length in points as twentieths of a point, rounded.</summary>
    public static int Twips(double points) => (int)Math.Round(points * 20, MidpointRounding.AwayFromZero);

    /// <summary>The text indent of a bullet list's level: 12 pt plus 18 pt per level.</summary>
    public static int BulletIndent(int level) => Twips(12) + level * Twips(18);

    /// <summary>The text indent of an ordered list's level: 22 pt plus 18 pt per level.</summary>
    public static int OrderedIndent(int level) => Twips(22) + level * Twips(18);

    // ---------------------------------------------------------------------------------------------
    // Styles
    // ---------------------------------------------------------------------------------------------

    /// <summary>Every style, for a page whose text is <paramref name="textWidth"/> twips wide.</summary>
    public static Styles Build(int textWidth)
    {
        var styles = new Styles(DocumentDefaults());

        styles.Append(ParagraphStyle(Normal, "Normal", basedOn: null, uiPriority: null, primary: true, isDefault: true));

        styles.Append(ParagraphStyle(Title, "Title", Normal, 10, primary: true,
            paragraph: p =>
            {
                p.KeepNext = new KeepNext();
                p.KeepLines = new KeepLines();
                p.SpacingBetweenLines = LineSpacing(before: 0, after: 4, line: 1.2);
            },
            run: r =>
            {
                r.Bold = new Bold();
                r.BoldComplexScript = new BoldComplexScript();
                r.Color = Color(Palette.Teal);
                r.FontSize = Size(20);
                r.FontSizeComplexScript = SizeCs(20);
            }));

        styles.Append(ParagraphStyle(Subtitle, "Subtitle", Normal, 11, primary: true,
            paragraph: p => p.SpacingBetweenLines = LineSpacing(before: 0, after: 4),
            run: r =>
            {
                r.Color = Color(Palette.Muted);
                r.FontSize = Size(11);
                r.FontSizeComplexScript = SizeCs(11);
            }));

        styles.Append(ParagraphStyle(DocumentKind, "Document Kind", Normal, null, custom: true,
            paragraph: p =>
            {
                p.KeepNext = new KeepNext();
                p.SpacingBetweenLines = LineSpacing(before: 6, after: 2);
            },
            run: r =>
            {
                r.Bold = new Bold();
                r.BoldComplexScript = new BoldComplexScript();
                r.Caps = new Caps();
                r.Color = Color(Palette.Muted);
                r.Spacing = new Spacing { Val = 14 };
                r.FontSize = Size(BenchmarkPdfStyle.SmallSize);
                r.FontSizeComplexScript = SizeCs(BenchmarkPdfStyle.SmallSize);
            }));

        double[] headingSizes = { BenchmarkPdfStyle.Heading1Size, BenchmarkPdfStyle.Heading2Size, BenchmarkPdfStyle.Heading3Size,
            BenchmarkPdfStyle.BaseSize, BenchmarkPdfStyle.BaseSize, BenchmarkPdfStyle.BaseSize };
        double[] headingBefore = { 8, 8, 4, 2, 2, 2 };
        for (int level = 1; level <= 6; level++)
        {
            int index = level - 1;
            styles.Append(ParagraphStyle(Heading(level), "heading " + level.ToString(CultureInfo.InvariantCulture), Normal, 9,
                primary: true, unhideWhenUsed: level > 1,
                paragraph: p =>
                {
                    p.KeepNext = new KeepNext();
                    p.KeepLines = new KeepLines();
                    if (level == 2)
                    {
                        p.ParagraphBorders = new ParagraphBorders
                        {
                            BottomBorder = new BottomBorder { Val = BorderValues.Single, Size = 6U, Space = 2U, Color = Palette.Gold }
                        };
                    }
                    p.SpacingBetweenLines = LineSpacing(before: headingBefore[index], after: BenchmarkPdfStyle.BlockSpacing, line: 1.2);
                    p.OutlineLevel = new OutlineLevel { Val = index };
                },
                run: r =>
                {
                    r.Bold = new Bold();
                    r.BoldComplexScript = new BoldComplexScript();
                    r.Color = Color(Palette.Teal);
                    r.FontSize = Size(headingSizes[index]);
                    r.FontSizeComplexScript = SizeCs(headingSizes[index]);
                }));
        }

        styles.Append(ParagraphStyle(TocHeading, "TOC Heading", Heading(1), 39, primary: true, semiHidden: true, unhideWhenUsed: true,
            paragraph: p =>
            {
                p.SpacingBetweenLines = LineSpacing(before: 0, after: 2);
                p.OutlineLevel = new OutlineLevel { Val = 9 };
            },
            run: r =>
            {
                r.FontSize = Size(BenchmarkPdfStyle.Heading3Size);
                r.FontSizeComplexScript = SizeCs(BenchmarkPdfStyle.Heading3Size);
            }));

        styles.Append(ParagraphStyle(Toc2, "toc 2", Normal, 39, unhideWhenUsed: true,
            paragraph: p =>
            {
                p.Tabs = new Tabs(new TabStop { Val = TabStopValues.Right, Leader = TabStopLeaderCharValues.Dot, Position = textWidth });
                p.SpacingBetweenLines = LineSpacing(before: 0, after: 2);
            },
            run: r => r.Color = Color(Palette.Teal)));

        styles.Append(ParagraphStyle(ListParagraph, "List Paragraph", Normal, 34, primary: true,
            paragraph: p =>
            {
                p.SpacingBetweenLines = LineSpacing(before: 0, after: 3);
                p.ContextualSpacing = new ContextualSpacing();
            }));

        styles.Append(ParagraphStyle(Quote, "Quote", Normal, 29, primary: true,
            paragraph: p =>
            {
                p.ParagraphBorders = new ParagraphBorders
                {
                    LeftBorder = new LeftBorder { Val = BorderValues.Single, Size = 12U, Space = 8U, Color = Palette.QuoteRule }
                };
                p.Indentation = new Indentation { Left = Twips(10).ToString(CultureInfo.InvariantCulture) };
            },
            run: r => r.Color = Color(Palette.Muted)));

        styles.Append(ParagraphStyle(CodeBlock, "Code Block", Normal, null, custom: true,
            paragraph: p =>
            {
                p.ParagraphBorders = new ParagraphBorders
                {
                    TopBorder = Edge<TopBorder>(Palette.CodeBorder, 4U, 4U),
                    LeftBorder = Edge<LeftBorder>(Palette.CodeBorder, 4U, 4U),
                    BottomBorder = Edge<BottomBorder>(Palette.CodeBorder, 4U, 4U),
                    RightBorder = Edge<RightBorder>(Palette.CodeBorder, 4U, 4U)
                };
                p.Shading = Fill(Palette.CodeBackground);
                p.SpacingBetweenLines = LineSpacing(before: 0, after: BenchmarkPdfStyle.BlockSpacing, line: BenchmarkPdfStyle.CodeLineHeight);
                p.Indentation = new Indentation
                {
                    Left = Twips(6).ToString(CultureInfo.InvariantCulture),
                    Right = Twips(6).ToString(CultureInfo.InvariantCulture)
                };
                p.ContextualSpacing = new ContextualSpacing();
            },
            run: r =>
            {
                r.RunFonts = FontsOf(BenchmarkWordFonts.MonoFamily);
                r.FontSize = Size(BenchmarkPdfStyle.CodeSize);
                r.FontSizeComplexScript = SizeCs(BenchmarkPdfStyle.CodeSize);
            }));

        styles.Append(RuleStyle(HorizontalRule, "Horizontal Rule", Palette.Rule, Normal));
        styles.Append(RuleStyle(ContentsRule, "Contents Rule", Palette.Gold, HorizontalRule));

        styles.Append(ParagraphStyle(SourceLine, "Source Line", Normal, null, custom: true,
            paragraph: p => p.SpacingBetweenLines = LineSpacing(before: 4, after: 4),
            run: r =>
            {
                r.Color = Color(Palette.Muted);
                r.FontSize = Size(BenchmarkPdfStyle.SmallSize);
                r.FontSizeComplexScript = SizeCs(BenchmarkPdfStyle.SmallSize);
            }));

        styles.Append(BannerStyle(ClassificationInternal, "Classification Internal", Palette.InternalBanner, Palette.InternalText));
        styles.Append(BannerStyle(ClassificationProvider, "Classification Provider", Palette.ProviderBanner, Palette.ProviderText));

        styles.Append(FrameStyle(Header, "header", textWidth,
            new BottomBorder { Val = BorderValues.Single, Size = 4U, Space = 3U, Color = Palette.Gold }));
        styles.Append(FrameStyle(Footer, "footer", textWidth,
            new TopBorder { Val = BorderValues.Single, Size = 4U, Space = 4U, Color = Palette.Rule }));

        styles.Append(CharacterStyle(DefaultParagraphFont, "Default Paragraph Font", basedOn: null, uiPriority: 1,
            isDefault: true, semiHidden: true, unhideWhenUsed: true));
        styles.Append(CharacterStyle(Hyperlink, "Hyperlink", DefaultParagraphFont, 99, unhideWhenUsed: true,
            run: r =>
            {
                r.Color = Color(Palette.Teal);
                r.Underline = new Underline { Val = UnderlineValues.Single };
            }));
        styles.Append(CharacterStyle(InlineCode, "Inline Code", DefaultParagraphFont, null, custom: true,
            run: r =>
            {
                r.RunFonts = FontsOf(BenchmarkWordFonts.MonoFamily);
                r.Shading = Fill(Palette.InlineCodeBackground);
            }));

        styles.Append(TableNormalStyle());
        styles.Append(NoListStyle());
        styles.Append(TableGridStyle());
        styles.Append(GnollBenchTableStyle());
        styles.Append(GnollBenchFactsStyle());

        return styles;
    }

    /// <summary>
    /// The base text: Source Sans 3 at 10.5 pt in ink, US English, 6 pt after each paragraph and a
    /// line height of 1.4. <c>Normal</c> inherits it unchanged.
    /// </summary>
    private static DocDefaults DocumentDefaults()
    {
        var run = new RunPropertiesBaseStyle
        {
            RunFonts = FontsOf(BenchmarkWordFonts.SansFamily),
            Color = Color(Palette.Ink),
            FontSize = Size(BenchmarkPdfStyle.BaseSize),
            FontSizeComplexScript = SizeCs(BenchmarkPdfStyle.BaseSize),
            Languages = new Languages { Val = BenchmarkWordRenderer.Language, EastAsia = BenchmarkWordRenderer.Language, Bidi = "ar-SA" }
        };
        var paragraph = new ParagraphPropertiesBaseStyle
        {
            SpacingBetweenLines = LineSpacing(before: 0, after: BenchmarkPdfStyle.BlockSpacing, line: BenchmarkPdfStyle.LineHeight)
        };

        return new DocDefaults
        {
            RunPropertiesDefault = new RunPropertiesDefault { RunPropertiesBaseStyle = run },
            ParagraphPropertiesDefault = new ParagraphPropertiesDefault { ParagraphPropertiesBaseStyle = paragraph }
        };
    }

    private static Style ParagraphStyle(
        string id, string name, string? basedOn, int? uiPriority,
        bool primary = false, bool custom = false, bool isDefault = false, bool semiHidden = false, bool unhideWhenUsed = false,
        Action<StyleParagraphProperties>? paragraph = null, Action<StyleRunProperties>? run = null)
    {
        var style = NewStyle(StyleValues.Paragraph, id, name, basedOn, uiPriority, primary, custom, isDefault, semiHidden, unhideWhenUsed);
        if (basedOn != null && id != Normal)
        {
            style.NextParagraphStyle = new NextParagraphStyle { Val = Normal };
        }
        if (paragraph != null)
        {
            var properties = new StyleParagraphProperties();
            paragraph(properties);
            style.StyleParagraphProperties = properties;
        }
        if (run != null)
        {
            var properties = new StyleRunProperties();
            run(properties);
            style.StyleRunProperties = properties;
        }
        return style;
    }

    private static Style CharacterStyle(
        string id, string name, string? basedOn, int? uiPriority,
        bool custom = false, bool isDefault = false, bool semiHidden = false, bool unhideWhenUsed = false,
        Action<StyleRunProperties>? run = null)
    {
        var style = NewStyle(StyleValues.Character, id, name, basedOn, uiPriority, primary: false, custom, isDefault, semiHidden, unhideWhenUsed);
        if (run != null)
        {
            var properties = new StyleRunProperties();
            run(properties);
            style.StyleRunProperties = properties;
        }
        return style;
    }

    private static Style NewStyle(
        StyleValues type, string id, string name, string? basedOn, int? uiPriority,
        bool primary, bool custom, bool isDefault, bool semiHidden, bool unhideWhenUsed)
    {
        var style = new Style { Type = type, StyleId = id };
        if (isDefault) style.Default = true;
        if (custom) style.CustomStyle = true;
        style.StyleName = new StyleName { Val = name };
        if (basedOn != null) style.BasedOn = new BasedOn { Val = basedOn };
        if (uiPriority != null) style.UIPriority = new UIPriority { Val = uiPriority.Value };
        if (semiHidden) style.SemiHidden = new SemiHidden();
        if (unhideWhenUsed) style.UnhideWhenUsed = new UnhideWhenUsed();
        if (primary) style.PrimaryStyle = new PrimaryStyle();
        return style;
    }

    /// <summary>An empty paragraph drawn as a 0.5 pt rule in <paramref name="color"/>, 4 pt tall with 6 pt after.</summary>
    private static Style RuleStyle(string id, string name, string color, string basedOn)
        => ParagraphStyle(id, name, basedOn, null, custom: true,
            paragraph: p =>
            {
                p.KeepLines = new KeepLines();
                p.ParagraphBorders = new ParagraphBorders
                {
                    BottomBorder = new BottomBorder { Val = BorderValues.Single, Size = 4U, Space = 1U, Color = color }
                };
                p.SpacingBetweenLines = new SpacingBetweenLines
                {
                    Before = "0",
                    After = Twips(BenchmarkPdfStyle.BlockSpacing).ToString(CultureInfo.InvariantCulture),
                    Line = "240",
                    LineRule = LineSpacingRuleValues.Auto
                };
            },
            run: r =>
            {
                r.FontSize = Size(4);
                r.FontSizeComplexScript = SizeCs(4);
            });

    /// <summary>
    /// The page-1 classification banner: bold text in <paramref name="text"/> on <paramref name="fill"/>,
    /// a 3 pt left bar in the text color and 4 pt of padding, drawn by borders in the fill color.
    /// </summary>
    private static Style BannerStyle(string id, string name, string fill, string text)
        => ParagraphStyle(id, name, Normal, null, custom: true,
            paragraph: p =>
            {
                p.KeepLines = new KeepLines();
                p.ParagraphBorders = new ParagraphBorders
                {
                    TopBorder = new TopBorder { Val = BorderValues.Single, Size = 4U, Space = 4U, Color = fill },
                    LeftBorder = new LeftBorder { Val = BorderValues.Single, Size = 24U, Space = 4U, Color = text },
                    BottomBorder = new BottomBorder { Val = BorderValues.Single, Size = 4U, Space = 4U, Color = fill },
                    RightBorder = new RightBorder { Val = BorderValues.Single, Size = 4U, Space = 4U, Color = fill }
                };
                p.Shading = Fill(fill);
                p.SpacingBetweenLines = LineSpacing(before: 8, after: 10);
                p.Indentation = new Indentation
                {
                    Left = Twips(7).ToString(CultureInfo.InvariantCulture),
                    Right = Twips(4.5).ToString(CultureInfo.InvariantCulture)
                };
            },
            run: r =>
            {
                r.Bold = new Bold();
                r.BoldComplexScript = new BoldComplexScript();
                r.Color = Color(text);
            });

    /// <summary>The running header or footer: 8.5 pt muted text with a center and a right tab, and its rule.</summary>
    private static Style FrameStyle(string id, string name, int textWidth, BorderType border)
        => ParagraphStyle(id, name, Normal, 99, unhideWhenUsed: true,
            paragraph: p =>
            {
                p.ParagraphBorders = border switch
                {
                    TopBorder top => new ParagraphBorders { TopBorder = top },
                    BottomBorder bottom => new ParagraphBorders { BottomBorder = bottom },
                    _ => null
                };
                p.Tabs = new Tabs(
                    new TabStop { Val = TabStopValues.Center, Position = textWidth / 2 },
                    new TabStop { Val = TabStopValues.Right, Position = textWidth });
                p.SpacingBetweenLines = new SpacingBetweenLines { Before = "0", After = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto };
            },
            run: r =>
            {
                r.Color = Color(Palette.Muted);
                r.FontSize = Size(BenchmarkPdfStyle.SmallSize);
                r.FontSizeComplexScript = SizeCs(BenchmarkPdfStyle.SmallSize);
            });

    /// <summary>Word's own default table style, "Normal Table".</summary>
    private static Style TableNormalStyle()
    {
        var style = NewStyle(StyleValues.Table, TableNormal, "Normal Table", null, 99,
            primary: false, custom: false, isDefault: true, semiHidden: true, unhideWhenUsed: true);
        style.StyleTableProperties = new StyleTableProperties
        {
            TableIndentation = new TableIndentation { Width = 0, Type = TableWidthUnitValues.Dxa },
            TableCellMarginDefault = CellMargins(top: 0, side: 5.4, bottom: 0)
        };
        return style;
    }

    private static Style NoListStyle()
        => NewStyle(StyleValues.Numbering, NoList, "No List", null, 99,
            primary: false, custom: false, isDefault: true, semiHidden: true, unhideWhenUsed: true);

    /// <summary>Word's own "Table Grid": single 0.5 pt borders on every edge.</summary>
    private static Style TableGridStyle()
    {
        var style = NewStyle(StyleValues.Table, TableGrid, "Table Grid", TableNormal, 39,
            primary: false, custom: false, isDefault: false, semiHidden: false, unhideWhenUsed: false);
        style.StyleParagraphProperties = new StyleParagraphProperties
        {
            SpacingBetweenLines = new SpacingBetweenLines { After = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto }
        };
        style.StyleTableProperties = new StyleTableProperties { TableBorders = TableEdges("auto") };
        return style;
    }

    /// <summary>
    /// The Markdown tables: hairline rules, 2.5 pt by 4 pt cell padding, 9.5 pt text, a shaded bold
    /// header row and zebra body rows. Word counts body rows from 1 after a header row, so its second
    /// band falls on the second, fourth… body row, as the PDF's zebra does.
    /// </summary>
    private static Style GnollBenchTableStyle()
    {
        var style = NewStyle(StyleValues.Table, GnollBenchTable, "GnollBench Table", TableGrid, null,
            primary: false, custom: true, isDefault: false, semiHidden: false, unhideWhenUsed: false);
        style.StyleParagraphProperties = new StyleParagraphProperties
        {
            SpacingBetweenLines = LineSpacing(before: 0, after: 0, line: 1.25)
        };
        style.StyleRunProperties = new StyleRunProperties
        {
            FontSize = Size(BenchmarkPdfStyle.TableCellSize),
            FontSizeComplexScript = SizeCs(BenchmarkPdfStyle.TableCellSize)
        };
        style.StyleTableProperties = new StyleTableProperties
        {
            TableStyleRowBandSize = new TableStyleRowBandSize { Val = 1 },
            TableStyleColumnBandSize = new TableStyleColumnBandSize { Val = 1 },
            TableBorders = TableEdges(Palette.Rule),
            TableCellMarginDefault = CellMargins(top: 2.5, side: 4, bottom: 2.5)
        };

        style.Append(new TableStyleProperties
        {
            Type = TableStyleOverrideValues.FirstRow,
            RunPropertiesBaseStyle = new RunPropertiesBaseStyle { Bold = new Bold(), BoldComplexScript = new BoldComplexScript() },
            TableStyleConditionalFormattingTableCellProperties = new TableStyleConditionalFormattingTableCellProperties
            {
                Shading = Fill(Palette.TableHeader)
            }
        });
        style.Append(new TableStyleProperties
        {
            Type = TableStyleOverrideValues.Band2Horizontal,
            TableStyleConditionalFormattingTableCellProperties = new TableStyleConditionalFormattingTableCellProperties
            {
                Shading = Fill(Palette.Zebra)
            }
        });
        return style;
    }

    /// <summary>The title block's facts: no rules, 1 pt above and below each cell, 9.5 pt text, muted labels.</summary>
    private static Style GnollBenchFactsStyle()
    {
        var style = NewStyle(StyleValues.Table, GnollBenchFacts, "GnollBench Facts", TableNormal, null,
            primary: false, custom: true, isDefault: false, semiHidden: false, unhideWhenUsed: false);
        style.StyleParagraphProperties = new StyleParagraphProperties
        {
            SpacingBetweenLines = new SpacingBetweenLines { Before = "0", After = "0" }
        };
        style.StyleRunProperties = new StyleRunProperties
        {
            FontSize = Size(BenchmarkPdfStyle.TableCellSize),
            FontSizeComplexScript = SizeCs(BenchmarkPdfStyle.TableCellSize)
        };
        style.StyleTableProperties = new StyleTableProperties
        {
            TableIndentation = new TableIndentation { Width = 0, Type = TableWidthUnitValues.Dxa },
            TableCellMarginDefault = CellMargins(top: 1, left: 0, bottom: 1, right: 6)
        };

        style.Append(new TableStyleProperties
        {
            Type = TableStyleOverrideValues.FirstColumn,
            RunPropertiesBaseStyle = new RunPropertiesBaseStyle { Color = Color(Palette.Muted) }
        });
        return style;
    }

    // ---------------------------------------------------------------------------------------------
    // Numbering
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The list numbering: one bullet definition that every bullet list shares, one decimal definition
    /// per delimiter in use (<c>%1.</c> and <c>%1)</c>), and one <c>w:num</c> per ordered list whose
    /// start override restarts it at its own first number.
    /// </summary>
    public static Numbering Numbering(IReadOnlyList<BenchmarkWordOrderedList> orderedLists)
    {
        var numbering = new Numbering();
        numbering.Append(AbstractList(BulletAbstractId, "4742B001", level => Bullets[level % Bullets.Length], NumberFormatValues.Bullet, BulletIndent));
        numbering.Append(AbstractList(PeriodAbstractId, "4742B002", level => "%" + (level + 1).ToString(CultureInfo.InvariantCulture) + ".",
            NumberFormatValues.Decimal, OrderedIndent));
        bool parenthesis = orderedLists.Any(l => l.Delimiter == ')');
        if (parenthesis)
        {
            numbering.Append(AbstractList(ParenthesisAbstractId, "4742B003", level => "%" + (level + 1).ToString(CultureInfo.InvariantCulture) + ")",
                NumberFormatValues.Decimal, OrderedIndent));
        }

        numbering.Append(new NumberingInstance { NumberID = BulletNumberingId, AbstractNumId = new AbstractNumId { Val = BulletAbstractId } });
        foreach (var list in orderedLists)
        {
            var instance = new NumberingInstance
            {
                NumberID = list.NumberingId,
                AbstractNumId = new AbstractNumId { Val = list.Delimiter == ')' ? ParenthesisAbstractId : PeriodAbstractId }
            };
            instance.Append(new LevelOverride
            {
                LevelIndex = list.Level,
                StartOverrideNumberingValue = new StartOverrideNumberingValue { Val = list.Start }
            });
            numbering.Append(instance);
        }
        return numbering;
    }

    private static AbstractNum AbstractList(
        int id, string nsid, Func<int, string> text, NumberFormatValues format, Func<int, int> indent)
    {
        var list = new AbstractNum
        {
            AbstractNumberId = id,
            Nsid = new Nsid { Val = nsid },
            MultiLevelType = new MultiLevelType { Val = MultiLevelValues.HybridMultilevel }
        };

        for (int level = 0; level < 9; level++)
        {
            var definition = new Level
            {
                LevelIndex = level,
                StartNumberingValue = new StartNumberingValue { Val = 1 },
                NumberingFormat = new NumberingFormat { Val = format },
                LevelText = new LevelText { Val = text(level) },
                LevelJustification = new LevelJustification { Val = LevelJustificationValues.Left },
                PreviousParagraphProperties = new PreviousParagraphProperties
                {
                    Indentation = new Indentation
                    {
                        Left = indent(level).ToString(CultureInfo.InvariantCulture),
                        Hanging = (indent(0)).ToString(CultureInfo.InvariantCulture)
                    }
                }
            };
            if (format == NumberFormatValues.Bullet)
            {
                definition.NumberingSymbolRunProperties = new NumberingSymbolRunProperties
                {
                    RunFonts = new RunFonts
                    {
                        Hint = FontTypeHintValues.Default,
                        Ascii = BenchmarkWordFonts.SansFamily,
                        HighAnsi = BenchmarkWordFonts.SansFamily
                    }
                };
            }
            list.Append(definition);
        }
        return list;
    }

    // ---------------------------------------------------------------------------------------------
    // Property helpers
    // ---------------------------------------------------------------------------------------------

    public static RunFonts FontsOf(string family) => new()
    {
        Ascii = family,
        HighAnsi = family,
        EastAsia = family,
        ComplexScript = family
    };

    public static Color Color(string rgb) => new() { Val = rgb };

    /// <summary>A font size in points as Word's half-points.</summary>
    public static FontSize Size(double points)
        => new() { Val = ((int)Math.Round(points * 2, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture) };

    public static FontSizeComplexScript SizeCs(double points)
        => new() { Val = ((int)Math.Round(points * 2, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture) };

    /// <summary>A plain fill, as Word writes paragraph, cell and text shading.</summary>
    public static Shading Fill(string rgb) => new() { Val = ShadingPatternValues.Clear, Color = "auto", Fill = rgb };

    /// <summary>Spacing in points; <paramref name="line"/> is a multiple of single line height.</summary>
    private static SpacingBetweenLines LineSpacing(double before, double after, double? line = null)
    {
        var spacing = new SpacingBetweenLines
        {
            Before = Twips(before).ToString(CultureInfo.InvariantCulture),
            After = Twips(after).ToString(CultureInfo.InvariantCulture)
        };
        if (line != null)
        {
            spacing.Line = ((int)Math.Round(line.Value * 240, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);
            spacing.LineRule = LineSpacingRuleValues.Auto;
        }
        return spacing;
    }

    /// <summary>A single border of <paramref name="size"/> eighths of a point, <paramref name="space"/> points from the text.</summary>
    private static T Edge<T>(string color, uint size, uint space) where T : BorderType, new()
        => new() { Val = BorderValues.Single, Size = size, Space = space, Color = color };

    /// <summary>Single 0.5 pt borders on every edge of a table and between its cells.</summary>
    private static TableBorders TableEdges(string color) => new()
    {
        TopBorder = Edge<TopBorder>(color, 4U, 0U),
        LeftBorder = Edge<LeftBorder>(color, 4U, 0U),
        BottomBorder = Edge<BottomBorder>(color, 4U, 0U),
        RightBorder = Edge<RightBorder>(color, 4U, 0U),
        InsideHorizontalBorder = Edge<InsideHorizontalBorder>(color, 4U, 0U),
        InsideVerticalBorder = Edge<InsideVerticalBorder>(color, 4U, 0U)
    };

    private static TableCellMarginDefault CellMargins(double top, double side, double bottom)
        => CellMargins(top, side, bottom, side);

    private static TableCellMarginDefault CellMargins(double top, double left, double bottom, double right) => new()
    {
        TopMargin = new TopMargin { Width = Twips(top).ToString(CultureInfo.InvariantCulture), Type = TableWidthUnitValues.Dxa },
        TableCellLeftMargin = new TableCellLeftMargin { Width = (short)Twips(left), Type = TableWidthValues.Dxa },
        BottomMargin = new BottomMargin { Width = Twips(bottom).ToString(CultureInfo.InvariantCulture), Type = TableWidthUnitValues.Dxa },
        TableCellRightMargin = new TableCellRightMargin { Width = (short)Twips(right), Type = TableWidthValues.Dxa }
    };
}
