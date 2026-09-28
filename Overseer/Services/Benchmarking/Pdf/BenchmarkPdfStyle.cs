namespace Overseer.Services.Benchmarking.Pdf;

using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

/// <summary>
/// The benchmark PDFs' colors, sizes and text styles, shared by the page frame
/// (<see cref="BenchmarkPdfRenderer"/>) and the Markdown body (<see cref="BenchmarkPdfMarkdownComposer"/>).
/// Brand teal is the text color of headings and links; the logo gold draws rules and accents and is
/// never a text color.
/// </summary>
internal static class BenchmarkPdfStyle
{
    public static readonly Color Ink = Hex(BenchmarkDocumentPalette.Ink);
    public static readonly Color Muted = Hex(BenchmarkDocumentPalette.Muted);
    public static readonly Color Teal = Hex(BenchmarkDocumentPalette.Teal);
    public static readonly Color Gold = Hex(BenchmarkDocumentPalette.Gold);
    public static readonly Color Rule = Hex(BenchmarkDocumentPalette.Rule);
    public static readonly Color TableHeader = Hex(BenchmarkDocumentPalette.TableHeader);
    public static readonly Color Zebra = Hex(BenchmarkDocumentPalette.Zebra);
    public static readonly Color CodeBackground = Hex(BenchmarkDocumentPalette.CodeBackground);
    public static readonly Color CodeBorder = Hex(BenchmarkDocumentPalette.CodeBorder);
    public static readonly Color InlineCodeBackground = Hex(BenchmarkDocumentPalette.InlineCodeBackground);
    public static readonly Color QuoteRule = Hex(BenchmarkDocumentPalette.QuoteRule);
    public static readonly Color MarkedBackground = Hex(BenchmarkDocumentPalette.MarkedBackground);
    public static readonly Color Watermark = Hex(BenchmarkDocumentPalette.Watermark);

    public static readonly Color InternalBanner = Hex(BenchmarkDocumentPalette.InternalBanner);
    public static readonly Color InternalText = Hex(BenchmarkDocumentPalette.InternalText);
    public static readonly Color ProviderBanner = Hex(BenchmarkDocumentPalette.ProviderBanner);
    public static readonly Color ProviderText = Hex(BenchmarkDocumentPalette.ProviderText);

    public const float BaseSize = 10.5f;
    public const float LineHeight = 1.4f;
    public const float SmallSize = 8.5f;
    public const float TitleSize = 20f;
    public const float Heading1Size = 16f;
    public const float Heading2Size = 13f;
    public const float Heading3Size = 11.5f;
    public const float TableCellSize = 9.5f;
    public const float CodeSize = 8.5f;
    public const float CodeLineHeight = 1.25f;

    /// <summary>Space between consecutive blocks of the body, in points.</summary>
    public const float BlockSpacing = 6f;

    /// <summary>Room a heading needs below it on its page, in points, or it moves to the next page.</summary>
    public const float KeepWithNextHeight = 72f;

    /// <summary>Hairline and table rule thickness, in points.</summary>
    public const float Hairline = 0.5f;

    public static Color BannerBackground(BenchmarkPdfClassification classification)
        => classification == BenchmarkPdfClassification.Internal ? InternalBanner : ProviderBanner;

    public static Color BannerText(BenchmarkPdfClassification classification)
        => classification == BenchmarkPdfClassification.Internal ? InternalText : ProviderText;

    /// <summary>The page's default style: text family with the fallback chain, base size, line height and ink.</summary>
    public static TextStyle Base() => TextStyle.Default
        .FontFamily(BenchmarkPdfResources.SansFamily, BenchmarkPdfResources.FallbackFamily)
        .FontSize(BaseSize)
        .LineHeight(LineHeight)
        .FontColor(Ink);

    /// <summary>Semibold text, in the family that holds the Semibold face, with the fallback chain.</summary>
    public static TextStyle Semibold(TextStyle style) => style
        .FontFamily(BenchmarkPdfResources.SemiboldFamily, BenchmarkPdfResources.SansFamily, BenchmarkPdfResources.FallbackFamily)
        .SemiBold();

    /// <summary>Code text: the monospace family with the fallback chain.</summary>
    public static TextStyle Mono(TextStyle style) => style
        .FontFamily(BenchmarkPdfResources.MonoFamily, BenchmarkPdfResources.FallbackFamily);

    public static T SemiboldSpan<T>(T span) where T : TextSpanDescriptor => span
        .FontFamily(BenchmarkPdfResources.SemiboldFamily, BenchmarkPdfResources.SansFamily, BenchmarkPdfResources.FallbackFamily)
        .SemiBold();

    public static T MonoSpan<T>(T span) where T : TextSpanDescriptor => span
        .FontFamily(BenchmarkPdfResources.MonoFamily, BenchmarkPdfResources.FallbackFamily);

    private static Color Hex(string rgb) => Color.FromHex("#" + rgb);
}
