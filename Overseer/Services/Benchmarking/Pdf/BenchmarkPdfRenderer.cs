namespace Overseer.Services.Benchmarking.Pdf;

using System;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

/// <summary>A source text above <see cref="BenchmarkPdfRenderer.MaxSourceCharacters"/>, refused before rendering.</summary>
public sealed class BenchmarkPdfSourceTooLargeException : Exception
{
    public BenchmarkPdfSourceTooLargeException(int characters)
        : base(BenchmarkPdfRenderer.TooLargeMessage(characters))
    {
        Characters = characters;
    }

    public int Characters { get; }
}

/// <summary>
/// Renders a benchmark document to a tagged PDF (PDF/A-3a and PDF/UA-1): Markdown for the report-pack
/// documents, the run report and the tool-call log, plain text for the run diagnostics.
///
/// <para>Static and stateless: nothing here holds a provider, a key or a service, so a PDF download
/// can no more reach a model than the Markdown download it is rendered from. The body is composed by
/// <see cref="BenchmarkPdfMarkdownComposer"/>; this class draws the page frame around it: the title
/// block on page 1, the running header from page 2, the footer and, for internal documents, the
/// watermark.</para>
///
/// <para>The content is reproducible: the same source and info always print the same text and
/// metadata. The bytes are not, because PDF/UA conformance makes QuestPDF embed a fresh document id.</para>
/// </summary>
public static class BenchmarkPdfRenderer
{
    /// <summary>The page layout's version, printed in the title block and footer as "PDF layout 2".</summary>
    public const int LayoutVersion = 2;

    /// <summary>The longest source text rendered; a longer one is refused before rendering starts.</summary>
    public const int MaxSourceCharacters = 6_000_000;

    public const string Author = "GnollBench (Overseer)";

    public const string Language = "en-US";

    /// <summary>Plain-text lines per text element, so a canceled render stops between chunks.</summary>
    private const int PlainTextChunkLines = 80;

    /// <summary>This build of Overseer, without the source-revision suffix.</summary>
    public static string OverseerVersion { get; } = ReadOverseerVersion();

    public static bool IsTooLarge(string? source) => source != null && source.Length > MaxSourceCharacters;

    public static string TooLargeMessage(int characters)
        => $"This document is too large for a PDF ({characters.ToString("N0", CultureInfo.InvariantCulture)} characters); download the Markdown instead.";

    /// <summary>SHA-256 of the UTF-8 text, lowercase hex.</summary>
    public static string Sha256(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty))).ToLowerInvariant();

    /// <summary>
    /// Markdown rendered as a PDF. Throws <see cref="BenchmarkPdfSourceTooLargeException"/> for a source
    /// above <see cref="MaxSourceCharacters"/>, and <see cref="OperationCanceledException"/> once the
    /// token is canceled.
    /// </summary>
    public static byte[] RenderMarkdown(string markdown, BenchmarkPdfDocumentInfo info, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(info);
        Guard(markdown);
        cancellationToken.ThrowIfCancellationRequested();
        BenchmarkPdfResources.EnsureRegistered();

        var sourced = info with { SourceSha256 = Sha256(markdown) };
        var prepared = BenchmarkPdfMarkdownComposer.Prepare(markdown, sourced.Title);
        bool contents = sourced.AllowTableOfContents && prepared.Contents.Count >= 4;

        return Generate(sourced, cancellationToken, body =>
        {
            if (contents)
            {
                body.Item().Element(c => BenchmarkPdfMarkdownComposer.ComposeTableOfContents(c, prepared));
            }
            body.Item().Element(c => BenchmarkPdfMarkdownComposer.ComposeBody(c, prepared, cancellationToken));
        });
    }

    /// <summary>
    /// Plain text rendered as one monospace block, wrapped anywhere, its line breaks kept. Refuses and
    /// cancels as <see cref="RenderMarkdown"/> does.
    /// </summary>
    public static byte[] RenderPlainText(string text, BenchmarkPdfDocumentInfo info, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(info);
        Guard(text);
        cancellationToken.ThrowIfCancellationRequested();
        BenchmarkPdfResources.EnsureRegistered();

        var sourced = info with { SourceSha256 = Sha256(text) };
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\t", "    ", StringComparison.Ordinal)
            .Split('\n');

        return Generate(sourced, cancellationToken, body =>
            body.Item().Element(c => PlainTextBlock(c, lines, cancellationToken)));
    }

    private static void Guard(string source)
    {
        if (source.Length > MaxSourceCharacters)
        {
            throw new BenchmarkPdfSourceTooLargeException(source.Length);
        }
    }

    private static byte[] Generate(BenchmarkPdfDocumentInfo info, CancellationToken cancellationToken, Action<ColumnDescriptor> body)
    {
        var created = new DateTimeOffset(DateTime.SpecifyKind(info.CreatedAtUtc, DateTimeKind.Utc));

        var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(info.Paper == BenchmarkPdfPaper.Letter ? PageSizes.Letter : PageSizes.A4);
                    page.MarginHorizontal(20, Unit.Millimetre);
                    page.MarginVertical(18, Unit.Millimetre);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(BenchmarkPdfStyle.Base());

                    if (info.Classification == BenchmarkPdfClassification.Internal)
                    {
                        page.Background().Element(Watermark);
                    }

                    page.Header().SkipOnce().Element(c => RunningHeader(c, info));

                    page.Content().Column(col =>
                    {
                        col.Spacing(10);
                        col.Item().Element(c => TitleBlock(c, info));
                        body(col);
                    });

                    page.Footer().Element(c => Footer(c, info));
                });
            })
            .WithMetadata(new DocumentMetadata
            {
                Title = info.Title,
                Author = Author,
                Subject = info.SubjectLine,
                Keywords = string.Join(", ", info.Keywords),
                Creator = "Overseer " + OverseerVersion,
                Language = Language,
                CreationDate = created,
                ModifiedDate = created
            })
            .WithSettings(new DocumentSettings
            {
                PDFA_Conformance = PDFA_Conformance.PDFA_3A,
                PDFUA_Conformance = PDFUA_Conformance.PDFUA_1
            });

        try
        {
            return document.GeneratePdf();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            // QuestPDF may wrap the composer's cancellation in its own exception type.
            throw new OperationCanceledException(cancellationToken);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Page frame
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Page 1's title block: the wide logo, the document kind, the title, the subject line, the facts,
    /// the source hash and layout version, and the classification banner, whose text states the
    /// classification so color is never the only signal.
    /// </summary>
    private static void TitleBlock(IContainer container, BenchmarkPdfDocumentInfo info)
    {
        container.Column(col =>
        {
            col.Spacing(4);

            col.Item().AlignLeft().Width(48, Unit.Millimetre)
                .SemanticImage("GnollBench")
                .Image(BenchmarkPdfResources.WideLogo.ToArray())
                .FitWidth();

            // Small capitals, drawn as spaced capitals at a small size.
            BenchmarkPdfStyle.SemiboldSpan(col.Item().PaddingTop(6).Text(info.DocumentKind.ToUpperInvariant()))
                .FontSize(BenchmarkPdfStyle.SmallSize)
                .LetterSpacing(0.08f)
                .FontColor(BenchmarkPdfStyle.Muted);

            BenchmarkPdfStyle.SemiboldSpan(col.Item().SemanticHeading1().Text(info.Title))
                .FontSize(BenchmarkPdfStyle.TitleSize)
                .LineHeight(1.2f)
                .FontColor(BenchmarkPdfStyle.Teal);

            if (!string.IsNullOrWhiteSpace(info.SubjectLine))
            {
                col.Item().Text(info.SubjectLine).FontSize(11).FontColor(BenchmarkPdfStyle.Muted);
            }

            if (info.Facts.Count > 0)
            {
                col.Item().PaddingTop(4).Element(c => Facts(c, info));
            }

            col.Item().Text($"Source {ShortHash(info.SourceSha256)} · PDF layout {LayoutVersion.ToString(CultureInfo.InvariantCulture)}")
                .FontSize(BenchmarkPdfStyle.SmallSize)
                .FontColor(BenchmarkPdfStyle.Muted);

            BenchmarkPdfStyle.SemiboldSpan(col.Item().PaddingTop(4)
                    .Background(BenchmarkPdfStyle.BannerBackground(info.Classification))
                    .BorderLeft(3).BorderColor(BenchmarkPdfStyle.BannerText(info.Classification))
                    .PaddingVertical(6).PaddingHorizontal(8)
                    .Text(info.ClassificationText))
                .FontColor(BenchmarkPdfStyle.BannerText(info.Classification));
        });
    }

    private static void Facts(IContainer container, BenchmarkPdfDocumentInfo info)
    {
        container.SemanticTable().Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(38, Unit.Millimetre);
                columns.RelativeColumn();
            });

            foreach (var fact in info.Facts)
            {
                table.Cell().SemanticHorizontalHeader().PaddingVertical(1)
                    .Text(fact.Label).FontSize(BenchmarkPdfStyle.TableCellSize).FontColor(BenchmarkPdfStyle.Muted);
                table.Cell().PaddingVertical(1)
                    .Text(fact.Value).FontSize(BenchmarkPdfStyle.TableCellSize);
            }
        });
    }

    /// <summary>Pages 2 onward: the emblem, "GnollBench · kind" and the subject line over a gold hairline. An artifact.</summary>
    private static void RunningHeader(IContainer container, BenchmarkPdfDocumentInfo info)
    {
        container.SemanticIgnore()
            .PaddingBottom(6)
            .BorderBottom(BenchmarkPdfStyle.Hairline).BorderColor(BenchmarkPdfStyle.Gold)
            .PaddingBottom(3)
            .DefaultTextStyle(s => s.FontSize(BenchmarkPdfStyle.SmallSize).FontColor(BenchmarkPdfStyle.Muted))
            .Row(row =>
            {
                row.Spacing(6);
                row.ConstantItem(7, Unit.Millimetre).Height(7, Unit.Millimetre)
                    .Image(BenchmarkPdfResources.SquareEmblem.ToArray())
                    .FitArea();
                row.RelativeItem().AlignMiddle().Text("GnollBench · " + info.DocumentKind);
                row.RelativeItem().AlignMiddle().Text(t =>
                {
                    t.AlignRight();
                    t.Span(info.SubjectLine);
                });
            });
    }

    /// <summary>Every page: the short classification, the source hash and layout version, and "Page X of Y". An artifact.</summary>
    private static void Footer(IContainer container, BenchmarkPdfDocumentInfo info)
    {
        container.SemanticIgnore()
            .BorderTop(BenchmarkPdfStyle.Hairline).BorderColor(BenchmarkPdfStyle.Rule)
            .PaddingTop(4)
            .DefaultTextStyle(s => s.FontSize(BenchmarkPdfStyle.SmallSize).FontColor(BenchmarkPdfStyle.Muted))
            .Row(row =>
            {
                // 3 : 4 : 2, so the longest short classification stays on one line.
                row.RelativeItem(3).Text(t =>
                {
                    BenchmarkPdfStyle.SemiboldSpan(t.Span(info.FooterClassification))
                        .FontColor(BenchmarkPdfStyle.BannerText(info.Classification));
                });
                row.RelativeItem(4).Text(t =>
                {
                    t.AlignCenter();
                    t.Span($"Source {ShortHash(info.SourceSha256)} · PDF layout {LayoutVersion.ToString(CultureInfo.InvariantCulture)}");
                });
                row.RelativeItem(2).Text(t =>
                {
                    t.AlignRight();
                    t.Span("Page ");
                    t.CurrentPageNumber();
                    t.Span(" of ");
                    t.TotalPages();
                });
            });
    }

    /// <summary>"INTERNAL", diagonal, 6 % gray, behind the content; an artifact, so screen readers and text extraction by structure skip it.</summary>
    private static void Watermark(IContainer container)
    {
        BenchmarkPdfStyle.SemiboldSpan(container.SemanticIgnore()
                .AlignCenter().AlignMiddle()
                .Rotate(-40)
                .Text("INTERNAL"))
            .FontSize(96)
            .LetterSpacing(0.06f)
            .FontColor(BenchmarkPdfStyle.Watermark);
    }

    private static void PlainTextBlock(IContainer container, string[] lines, CancellationToken cancellationToken)
    {
        container.Background(BenchmarkPdfStyle.CodeBackground)
            .Border(BenchmarkPdfStyle.Hairline, BenchmarkPdfStyle.CodeBorder)
            .PaddingVertical(4).PaddingHorizontal(6)
            .DefaultTextStyle(s => BenchmarkPdfStyle.Mono(s)
                .FontSize(BenchmarkPdfStyle.CodeSize)
                .LineHeight(BenchmarkPdfStyle.CodeLineHeight))
            .Column(col =>
            {
                for (int i = 0; i < lines.Length; i += PlainTextChunkLines)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string chunk = string.Join('\n', lines, i, Math.Min(PlainTextChunkLines, lines.Length - i));
                    col.Item().Text(t => t.Span(chunk).BreakAnywhere());
                }
            });
    }

    private static string ShortHash(string sha256) => sha256.Length > 16 ? sha256[..16] : sha256;

    private static string ReadOverseerVersion()
    {
        string? informational = typeof(BenchmarkPdfRenderer).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        return string.IsNullOrWhiteSpace(informational) ? "unknown" : informational.Split('+')[0];
    }
}
