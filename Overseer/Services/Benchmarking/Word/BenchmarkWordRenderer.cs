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
using Overseer.Services.Benchmarking.Pdf;
using A = DocumentFormat.OpenXml.Drawing;
using Ap = DocumentFormat.OpenXml.ExtendedProperties;
using Cp = DocumentFormat.OpenXml.CustomProperties;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using Ovml = DocumentFormat.OpenXml.Vml.Office;
using Palette = Overseer.Services.Benchmarking.Pdf.BenchmarkDocumentPalette;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using V = DocumentFormat.OpenXml.Vml;
using Vt = DocumentFormat.OpenXml.VariantTypes;
using W10 = DocumentFormat.OpenXml.Vml.Wordprocessing;
using WordStyles = Overseer.Services.Benchmarking.Word.BenchmarkWordStyles;
using Writer = Overseer.Services.Benchmarking.Word.BenchmarkWordMarkdownWriter;

/// <summary>A source text above <see cref="BenchmarkWordRenderer.MaxSourceCharacters"/>, refused before rendering.</summary>
public sealed class BenchmarkWordSourceTooLargeException : Exception
{
    public BenchmarkWordSourceTooLargeException(int characters)
        : base(BenchmarkWordRenderer.TooLargeMessage(characters))
    {
        Characters = characters;
    }

    public int Characters { get; }
}

/// <summary>
/// Renders a benchmark document to a Word document (.docx) styled like its PDF: Markdown for the
/// report-pack documents, the run report and the tool-call log, plain text for the run diagnostics.
///
/// <para>The package is built with the Open XML SDK the way Word builds its own documents, so it
/// edits like one: named styles (<see cref="BenchmarkWordStyles"/>), real lists and tables, a real
/// table of contents, header and footer fields, and the text fonts embedded
/// (<see cref="BenchmarkWordFonts"/>). The body is written by <see cref="BenchmarkWordMarkdownWriter"/>;
/// this class adds the frame around it: the title block on page 1, the running header from page 2,
/// the footer and, for internal documents, Word's own text watermark.</para>
///
/// <para>Static and stateless, as <see cref="BenchmarkPdfRenderer"/> is: nothing here holds a provider,
/// a key or a service. The document information is <see cref="BenchmarkPdfDocumentInfo"/>, which is
/// format-neutral, and every date in the package is its stored creation date.</para>
/// </summary>
public static class BenchmarkWordRenderer
{
    /// <summary>The page layout's version, printed in the title block and footer as "Word layout 1".</summary>
    public const int LayoutVersion = 1;

    /// <summary>The longest source text rendered, the PDF's limit; a longer one is refused before rendering starts.</summary>
    public const int MaxSourceCharacters = BenchmarkPdfRenderer.MaxSourceCharacters;

    public const string ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    public const string Author = BenchmarkPdfRenderer.Author;

    public const string Language = BenchmarkPdfRenderer.Language;

    /// <summary>The custom document property holding the footer's short classification.</summary>
    public const string ClassificationProperty = "GnollBench Classification";

    /// <summary>The custom document property holding the SHA-256 of the source text.</summary>
    public const string SourceHashProperty = "GnollBench Source SHA-256";

    /// <summary>The id prefix Word gives a text watermark's shape, by which Remove Watermark finds it.</summary>
    public const string WatermarkShapePrefix = "PowerPlusWaterMarkObject";

    // Page geometry in twips: A4 and US Letter, 20 mm side margins, 18 mm top and bottom margins,
    // and the header and footer 7.5 mm from the page edge.
    private const int A4Width = 11906;
    private const int A4Height = 16838;
    private const int LetterWidth = 12240;
    private const int LetterHeight = 15840;
    private const int SideMargin = 1134;
    private const int VerticalMargin = 1020;
    private const int HeaderDistance = 425;

    /// <summary>English Metric Units per millimeter.</summary>
    private const long EmuPerMillimeter = 36000;

    private const string RelationshipsNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static readonly Lazy<(byte[] WideLogo, byte[] Emblem)> Logos = new(ReadLogos);

    public static bool IsTooLarge(string? source) => source != null && source.Length > MaxSourceCharacters;

    public static string TooLargeMessage(int characters)
        => $"This document is too large for a Word document ({characters.ToString("N0", CultureInfo.InvariantCulture)} characters); download the Markdown instead.";

    /// <summary>
    /// Markdown rendered as a Word document. Throws <see cref="BenchmarkWordSourceTooLargeException"/>
    /// for a source above <see cref="MaxSourceCharacters"/>, and <see cref="OperationCanceledException"/>
    /// once the token is canceled.
    /// </summary>
    public static byte[] RenderMarkdown(string markdown, BenchmarkPdfDocumentInfo info, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(info);
        Guard(markdown);
        ct.ThrowIfCancellationRequested();

        var sourced = info with { SourceSha256 = BenchmarkPdfRenderer.Sha256(markdown) };
        var prepared = BenchmarkPdfMarkdownComposer.Prepare(markdown, sourced.Title);
        bool contents = sourced.AllowTableOfContents && prepared.Contents.Count >= 4;

        return Generate(sourced, ct, (body, part, textWidth) =>
        {
            if (contents)
            {
                body.Append(Writer.TableOfContents(prepared));
                body.Append(new Paragraph(Writer.Properties(WordStyles.ContentsRule)));
            }

            var writer = new Writer(part, prepared, textWidth, ct);
            writer.WriteBody(body);
            return writer.OrderedLists;
        });
    }

    /// <summary>
    /// Plain text rendered as one <c>Code Block</c> paragraph per line, its line breaks kept. Refuses and
    /// cancels as <see cref="RenderMarkdown"/> does.
    /// </summary>
    public static byte[] RenderPlainText(string text, BenchmarkPdfDocumentInfo info, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(info);
        Guard(text);
        ct.ThrowIfCancellationRequested();

        var sourced = info with { SourceSha256 = BenchmarkPdfRenderer.Sha256(text) };
        string[] lines = Writer.CodeLines(text);

        return Generate(sourced, ct, (body, _, _) =>
        {
            Writer.AppendCode(body, lines, null, ct);
            return Array.Empty<BenchmarkWordOrderedList>();
        });
    }

    private static void Guard(string source)
    {
        if (source.Length > MaxSourceCharacters)
        {
            throw new BenchmarkWordSourceTooLargeException(source.Length);
        }
    }

    /// <summary>
    /// The package around a body: the title block, the body <paramref name="write"/> appends (returning
    /// its ordered lists), the section with its headers and footers, and every supporting part, saved
    /// when the package is disposed.
    /// </summary>
    private static byte[] Generate(
        BenchmarkPdfDocumentInfo info, CancellationToken ct,
        Func<Body, MainDocumentPart, int, IReadOnlyList<BenchmarkWordOrderedList>> write)
    {
        bool letter = info.Paper == BenchmarkPdfPaper.Letter;
        int pageWidth = letter ? LetterWidth : A4Width;
        int pageHeight = letter ? LetterHeight : A4Height;
        int textWidth = pageWidth - 2 * SideMargin;

        using var stream = new MemoryStream();
        using (var package = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = package.AddMainDocumentPart();

            var stylesPart = main.AddNewPart<StyleDefinitionsPart>();
            stylesPart.Styles = WordStyles.Build(textWidth);

            var settingsPart = main.AddNewPart<DocumentSettingsPart>();
            settingsPart.Settings = Settings();

            var fontTablePart = main.AddNewPart<FontTablePart>();
            fontTablePart.Fonts = BenchmarkWordFonts.FontTable(fontTablePart);

            var body = new Body();
            TitleBlock(body, main, info, textWidth);
            var orderedLists = write(body, main, textWidth);
            ct.ThrowIfCancellationRequested();

            if (body.LastChild is not Paragraph)
            {
                body.Append(new Paragraph());
            }

            var numberingPart = main.AddNewPart<NumberingDefinitionsPart>();
            numberingPart.Numbering = WordStyles.Numbering(orderedLists);

            body.Append(Section(main, info, pageWidth, pageHeight, textWidth));

            var document = new Document(body);
            DeclareNamespaces(document);
            main.Document = document;

            WriteCoreProperties(package, info);
            package.AddExtendedFilePropertiesPart().Properties = new Ap.Properties(
                new Ap.Application(Writer.XmlSafe("Overseer " + BenchmarkPdfRenderer.OverseerVersion)));
            package.AddCustomFilePropertiesPart().Properties = CustomProperties(info);
        }
        return stream.ToArray();
    }

    // ---------------------------------------------------------------------------------------------
    // Page frame
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Page 1's title block: the wide logo, the document kind, the title, the subject line, the facts,
    /// the source hash and layout version, and the classification banner, whose text states the
    /// classification so color is never the only signal.
    /// </summary>
    private static void TitleBlock(Body body, MainDocumentPart main, BenchmarkPdfDocumentInfo info, int textWidth)
    {
        var logo = main.AddImagePart(ImagePartType.Png);
        Feed(logo, Logos.Value.WideLogo);

        long logoWidth = 48 * EmuPerMillimeter;
        long logoHeight = (long)Math.Round(logoWidth * 256.0 / 978.0);
        var logoParagraph = new Paragraph(new ParagraphProperties
        {
            KeepNext = new KeepNext(),
            SpacingBetweenLines = new SpacingBetweenLines { Before = "0", After = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto }
        });
        logoParagraph.Append(new Run(Picture(main.GetIdOfPart(logo), 1U, "GnollBench logo", "GnollBench", logoWidth, logoHeight)));
        body.Append(logoParagraph);

        body.Append(StyledParagraph(WordStyles.DocumentKind, info.DocumentKind));
        body.Append(StyledParagraph(WordStyles.Title, info.Title));
        if (!string.IsNullOrWhiteSpace(info.SubjectLine))
        {
            body.Append(StyledParagraph(WordStyles.Subtitle, info.SubjectLine));
        }
        if (info.Facts.Count > 0)
        {
            body.Append(Facts(info, textWidth));
        }
        body.Append(StyledParagraph(WordStyles.SourceLine, SourceText(info)));
        body.Append(StyledParagraph(
            info.Classification == BenchmarkPdfClassification.Internal ? WordStyles.ClassificationInternal : WordStyles.ClassificationProvider,
            info.ClassificationText));
    }

    /// <summary>The facts as a borderless two-column table, labels in its first column.</summary>
    private static Table Facts(BenchmarkPdfDocumentInfo info, int textWidth)
    {
        int labelWidth = (int)Math.Round(38 * 1440 / 25.4);
        int valueWidth = textWidth - labelWidth;

        var table = new Table(
            new TableProperties
            {
                TableStyle = new TableStyle { Val = WordStyles.GnollBenchFacts },
                TableWidth = new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
                TableLayout = new TableLayout { Type = TableLayoutValues.Fixed },
                TableLook = new TableLook
                {
                    Val = "0680",
                    FirstRow = false,
                    LastRow = false,
                    FirstColumn = true,
                    LastColumn = false,
                    NoHorizontalBand = true,
                    NoVerticalBand = true
                }
            },
            new TableGrid(
                new GridColumn { Width = labelWidth.ToString(CultureInfo.InvariantCulture) },
                new GridColumn { Width = valueWidth.ToString(CultureInfo.InvariantCulture) }));

        foreach (var fact in info.Facts)
        {
            table.Append(new TableRow(
                FactCell(fact.Label, labelWidth),
                FactCell(fact.Value, valueWidth)));
        }
        return table;
    }

    private static TableCell FactCell(string text, int width)
    {
        var paragraph = new Paragraph();
        if (!string.IsNullOrEmpty(text))
        {
            paragraph.Append(new Run(Writer.TextOf(text)));
        }
        return new TableCell(
            new TableCellProperties
            {
                TableCellWidth = new TableCellWidth { Width = width.ToString(CultureInfo.InvariantCulture), Type = TableWidthUnitValues.Dxa }
            },
            paragraph);
    }

    /// <summary>The section: the paper, the margins, the headers and footers, and a first page with its own header.</summary>
    private static SectionProperties Section(MainDocumentPart main, BenchmarkPdfDocumentInfo info, int pageWidth, int pageHeight, int textWidth)
    {
        bool watermark = info.Classification == BenchmarkPdfClassification.Internal;

        var firstHeader = main.AddNewPart<HeaderPart>();
        firstHeader.Header = FirstHeader(watermark);

        var defaultHeader = main.AddNewPart<HeaderPart>();
        var emblem = defaultHeader.AddImagePart(ImagePartType.Png);
        Feed(emblem, Logos.Value.Emblem);
        defaultHeader.Header = RunningHeader(info, defaultHeader.GetIdOfPart(emblem), watermark);

        var defaultFooter = main.AddNewPart<FooterPart>();
        defaultFooter.Footer = Footer(info);
        var firstFooter = main.AddNewPart<FooterPart>();
        firstFooter.Footer = Footer(info);

        return new SectionProperties(
            new HeaderReference { Type = HeaderFooterValues.Default, Id = main.GetIdOfPart(defaultHeader) },
            new FooterReference { Type = HeaderFooterValues.Default, Id = main.GetIdOfPart(defaultFooter) },
            new HeaderReference { Type = HeaderFooterValues.First, Id = main.GetIdOfPart(firstHeader) },
            new FooterReference { Type = HeaderFooterValues.First, Id = main.GetIdOfPart(firstFooter) },
            new PageSize { Width = (uint)pageWidth, Height = (uint)pageHeight },
            new PageMargin
            {
                Top = VerticalMargin,
                Right = (uint)SideMargin,
                Bottom = VerticalMargin,
                Left = (uint)SideMargin,
                Header = (uint)HeaderDistance,
                Footer = (uint)HeaderDistance,
                Gutter = 0U
            },
            new Columns { Space = "720" },
            new TitlePage(),
            new DocGrid { LinePitch = 360 });
    }

    /// <summary>Page 1's header: empty, holding only the watermark on an internal document.</summary>
    private static Header FirstHeader(bool watermark)
    {
        var paragraph = new Paragraph();
        if (watermark)
        {
            paragraph.Append(Watermark(1));
        }
        var header = new Header(paragraph);
        DeclareNamespaces(header);
        return header;
    }

    /// <summary>Pages 2 onward: the emblem, "GnollBench · kind" and, at the right tab, the subject line.</summary>
    private static Header RunningHeader(BenchmarkPdfDocumentInfo info, string emblemId, bool watermark)
    {
        var paragraph = new Paragraph(Writer.Properties(WordStyles.Header));
        if (watermark)
        {
            paragraph.Append(Watermark(2));
        }

        long size = 7 * EmuPerMillimeter;
        paragraph.Append(new Run(Picture(emblemId, 2U, "GnollBench emblem", string.Empty, size, size)));
        paragraph.Append(new Run(Writer.TextOf(" GnollBench · " + info.DocumentKind)));
        paragraph.Append(new Run(new TabChar(), new TabChar()));
        paragraph.Append(new Run(Writer.TextOf(info.SubjectLine)));

        var header = new Header(paragraph);
        DeclareNamespaces(header);
        return header;
    }

    /// <summary>Every page: the short classification, the source hash and layout version, and "Page X of Y" in fields.</summary>
    private static Footer Footer(BenchmarkPdfDocumentInfo info)
    {
        var classification = new Run(Writer.TextOf(info.FooterClassification))
        {
            RunProperties = new RunProperties
            {
                Bold = new Bold(),
                Color = WordStyles.Color(info.Classification == BenchmarkPdfClassification.Internal ? Palette.InternalText : Palette.ProviderText)
            }
        };

        var paragraph = new Paragraph(
            Writer.Properties(WordStyles.Footer),
            classification,
            new Run(new TabChar()),
            new Run(Writer.TextOf(SourceText(info))),
            new Run(new TabChar()),
            new Run(Writer.TextOf("Page ")),
            new SimpleField(new Run(new Text("1"))) { Instruction = " PAGE " },
            new Run(Writer.TextOf(" of ")),
            new SimpleField(new Run(new Text("1"))) { Instruction = " NUMPAGES " });

        var footer = new Footer(paragraph);
        footer.AddNamespaceDeclaration("r", RelationshipsNamespace);
        return footer;
    }

    /// <summary>
    /// "INTERNAL" as the markup Word writes for Design, Watermark, Custom watermark, Text: a VML
    /// text-path shape in the header, centered on the page behind the text, rotated 40° counterclockwise,
    /// in 6 % gray. Its id prefix is what Word's Remove Watermark looks for.
    /// </summary>
    private static Run Watermark(int index)
    {
        string[] formulas =
        {
            "sum #0 0 10800", "prod #0 2 1", "sum 21600 0 @1", "sum 0 0 @2", "sum 21600 0 @3", "if @0 @3 0",
            "if @0 21600 @1", "if @0 0 @2", "if @0 @4 21600", "mid @5 @6", "mid @8 @5", "mid @7 @8", "mid @6 @7",
            "sum @6 0 @5"
        };

        var shapeType = new V.Shapetype(
            new V.Formulas(formulas.Select(f => new V.Formula { Equation = f })),
            new V.Path
            {
                AllowTextPath = true,
                ConnectionPointType = Ovml.ConnectValues.Custom,
                ConnectionPoints = "@9,0;@10,10800;@11,21600;@12,10800",
                ConnectAngles = "270,180,90,0"
            },
            new V.TextPath { On = true, FitShape = true },
            new V.ShapeHandles(new V.ShapeHandle { Position = "#0,bottomRight", XRange = "6629,14971" }),
            new Ovml.Lock { Extension = V.ExtensionHandlingBehaviorValues.Edit, TextLock = true, ShapeType = true })
        {
            Id = "_x0000_t136",
            CoordinateSize = "21600,21600",
            OptionalNumber = 136,
            Adjustment = "10800",
            EdgePath = "m@7,l@8,m@5,21600l@6,21600e"
        };

        var shape = new V.Shape(
            new V.TextPath
            {
                Style = "font-family:\"" + BenchmarkWordFonts.SansFamily + "\";font-size:1pt",
                String = "INTERNAL"
            },
            new W10.TextWrap { AnchorX = W10.HorizontalAnchorValues.Margin, AnchorY = W10.VerticalAnchorValues.Margin })
        {
            Id = WatermarkShapePrefix + index.ToString(CultureInfo.InvariantCulture),
            OptionalString = "_x0000_s" + (2048 + index).ToString(CultureInfo.InvariantCulture),
            Type = "#_x0000_t136",
            Style = "position:absolute;margin-left:0;margin-top:0;width:460pt;height:115pt;rotation:320;z-index:-251657216;"
                + "mso-position-horizontal:center;mso-position-horizontal-relative:margin;"
                + "mso-position-vertical:center;mso-position-vertical-relative:margin",
            AllowInCell = false,
            FillColor = "#" + Palette.Watermark,
            Stroked = false
        };

        return new Run(new RunProperties { NoProof = new NoProof() }, new Picture(shapeType, shape));
    }

    /// <summary>An inline PNG, <paramref name="description"/> its alternative text (empty for a decorative picture).</summary>
    private static Drawing Picture(string relationshipId, uint id, string name, string description, long width, long height)
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
                                new PIC.NonVisualDrawingProperties { Id = 0U, Name = name + ".png", Description = description },
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

    private static Paragraph StyledParagraph(string style, string text)
    {
        var paragraph = new Paragraph(Writer.Properties(style));
        if (!string.IsNullOrEmpty(text))
        {
            paragraph.Append(new Run(Writer.TextOf(text)));
        }
        return paragraph;
    }

    private static string SourceText(BenchmarkPdfDocumentInfo info)
        => $"Source {ShortHash(info.SourceSha256)} · Word layout {LayoutVersion.ToString(CultureInfo.InvariantCulture)}";

    private static string ShortHash(string sha256) => sha256.Length > 16 ? sha256[..16] : sha256;

    // ---------------------------------------------------------------------------------------------
    // Package parts
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Word's defaults for a new document: fonts embedded whole (no subsetting, so the text stays
    /// editable in them), half-inch default tabs, no punctuation compression, US English, and the
    /// Word 2013+ compatibility mode, so Word opens the file without a Compatibility Mode banner.
    /// </summary>
    private static Settings Settings()
    {
        const string wordUri = "http://schemas.microsoft.com/office/word";
        CompatibilitySetting Compat(CompatSettingNameValues name, string value) => new() { Name = name, Uri = wordUri, Val = value };

        return new Settings(
            new EmbedTrueTypeFonts(),
            new DefaultTabStop { Val = (short)720 },
            new CharacterSpacingControl { Val = CharacterSpacingValues.DoNotCompress },
            new Compatibility(
                Compat(CompatSettingNameValues.CompatibilityMode, "15"),
                Compat(CompatSettingNameValues.OverrideTableStyleFontSizeAndJustification, "1"),
                Compat(CompatSettingNameValues.EnableOpenTypeFeatures, "1"),
                Compat(CompatSettingNameValues.DoNotFlipMirrorIndents, "1"),
                Compat(CompatSettingNameValues.DifferentiateMultirowTableHeaders, "1")),
            new ThemeFontLanguages { Val = Language });
    }

    /// <summary>
    /// <c>docProps/core.xml</c>, written as Word writes it: title, subject, creator, keywords, last
    /// modified by, language, and the stored creation date as both created and modified.
    /// </summary>
    private static void WriteCoreProperties(WordprocessingDocument package, BenchmarkPdfDocumentInfo info)
    {
        const string cp = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
        const string dc = "http://purl.org/dc/elements/1.1/";
        const string dcterms = "http://purl.org/dc/terms/";
        const string dcmitype = "http://purl.org/dc/dcmitype/";
        const string xsi = "http://www.w3.org/2001/XMLSchema-instance";

        string created = DateTime.SpecifyKind(info.CreatedAtUtc, DateTimeKind.Utc)
            .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        var part = package.AddCoreFilePropertiesPart();
        using var stream = part.GetStream(FileMode.Create, FileAccess.Write);
        using var xml = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false) });

        xml.WriteStartDocument(true);
        xml.WriteStartElement("cp", "coreProperties", cp);
        xml.WriteAttributeString("xmlns", "dc", null, dc);
        xml.WriteAttributeString("xmlns", "dcterms", null, dcterms);
        xml.WriteAttributeString("xmlns", "dcmitype", null, dcmitype);
        xml.WriteAttributeString("xmlns", "xsi", null, xsi);

        xml.WriteElementString("dc", "title", dc, Writer.XmlSafe(info.Title));
        xml.WriteElementString("dc", "subject", dc, Writer.XmlSafe(info.SubjectLine));
        xml.WriteElementString("dc", "creator", dc, Author);
        xml.WriteElementString("cp", "keywords", cp, Writer.XmlSafe(string.Join(", ", info.Keywords)));
        xml.WriteElementString("cp", "lastModifiedBy", cp, Author);
        xml.WriteElementString("dc", "language", dc, Language);
        foreach (string name in new[] { "created", "modified" })
        {
            xml.WriteStartElement("dcterms", name, dcterms);
            xml.WriteAttributeString("xsi", "type", xsi, "dcterms:W3CDTF");
            xml.WriteString(created);
            xml.WriteEndElement();
        }

        xml.WriteEndElement();
        xml.WriteEndDocument();
    }

    /// <summary>The footer's classification and the source's SHA-256, as custom document properties.</summary>
    private static Cp.Properties CustomProperties(BenchmarkPdfDocumentInfo info)
    {
        // The format id every custom property of an Office document carries.
        const string formatId = "{D5CDD505-2E9C-101B-9397-08002B2CF9AE}";

        var properties = new Cp.Properties(
            new Cp.CustomDocumentProperty(new Vt.VTLPWSTR(info.FooterClassification))
            {
                FormatId = formatId,
                PropertyId = 2,
                Name = ClassificationProperty
            },
            new Cp.CustomDocumentProperty(new Vt.VTLPWSTR(info.SourceSha256))
            {
                FormatId = formatId,
                PropertyId = 3,
                Name = SourceHashProperty
            });
        properties.AddNamespaceDeclaration("vt", "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes");
        return properties;
    }

    /// <summary>The namespaces a document or header uses, declared once on its root as Word does.</summary>
    private static void DeclareNamespaces(OpenXmlElement root)
    {
        root.AddNamespaceDeclaration("r", RelationshipsNamespace);
        root.AddNamespaceDeclaration("wp", "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing");
        root.AddNamespaceDeclaration("a", "http://schemas.openxmlformats.org/drawingml/2006/main");
        root.AddNamespaceDeclaration("pic", "http://schemas.openxmlformats.org/drawingml/2006/picture");
        root.AddNamespaceDeclaration("v", "urn:schemas-microsoft-com:vml");
        root.AddNamespaceDeclaration("o", "urn:schemas-microsoft-com:office:office");
        root.AddNamespaceDeclaration("w10", "urn:schemas-microsoft-com:office:word");
    }

    private static void Feed(OpenXmlPart part, byte[] data)
    {
        using var stream = new MemoryStream(data, writable: false);
        part.FeedData(stream);
    }

    /// <summary>The two PNG logos under <c>Resources/Word</c>; Word and LibreOffice do not open WebP pictures.</summary>
    private static (byte[] WideLogo, byte[] Emblem) ReadLogos()
    {
        var assembly = typeof(BenchmarkWordRenderer).Assembly;
        string[] names = assembly.GetManifestResourceNames();
        return (
            BenchmarkPdfResources.ReadResource(assembly, names, "gnollbench-wide-v3-h256.png"),
            BenchmarkPdfResources.ReadResource(assembly, names, "gnollbench-logo-v3-256.png"));
    }
}
