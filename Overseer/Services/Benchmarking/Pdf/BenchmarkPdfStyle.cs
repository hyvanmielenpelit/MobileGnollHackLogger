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
    public static readonly Color Ink = Color.FromHex("#1B1B1B");
    public static readonly Color Muted = Color.FromHex("#55595E");
    public static readonly Color Teal = Color.FromHex("#2F7088");
    public static readonly Color Gold = Color.FromHex("#AA8E47");
    public static readonly Color Rule = Color.FromHex("#C9CED3");
    public static readonly Color TableHeader = Color.FromHex("#EEF1F3");
    public static readonly Color Zebra = Color.FromHex("#F7F8F9");
    public static readonly Color CodeBackground = Color.FromHex("#F5F5F5");
    public static readonly Color CodeBorder = Color.FromHex("#D4D4D4");
    public static readonly Color InlineCodeBackground = Color.FromHex("#EEF1F3");
    public static readonly Color QuoteRule = Color.FromHex("#B8BDC2");
    public static readonly Color MarkedBackground = Color.FromHex("#FFF1B8");
    public static readonly Color Watermark = Color.FromHex("#F0F0F0");

    public static readonly Color InternalBanner = Color.FromHex("#FBE9E9");
    public static readonly Color InternalText = Color.FromHex("#8A1C1C");
    public static readonly Color ProviderBanner = Color.FromHex("#FFF4DC");
    public static readonly Color ProviderText = Color.FromHex("#7A5200");

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
}
