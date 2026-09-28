namespace Overseer.Services.Benchmarking.Pdf;

/// <summary>
/// The benchmark documents' colors as six-digit hex without <c>#</c>, shared by the PDF
/// (<see cref="BenchmarkPdfStyle"/>) and the Word document (<c>BenchmarkWordStyles</c>), so both
/// formats print the same palette.
/// </summary>
internal static class BenchmarkDocumentPalette
{
    public const string Ink = "1B1B1B";
    public const string Muted = "55595E";
    public const string Teal = "2F7088";
    public const string Gold = "AA8E47";
    public const string Rule = "C9CED3";
    public const string TableHeader = "EEF1F3";
    public const string Zebra = "F7F8F9";
    public const string CodeBackground = "F5F5F5";
    public const string CodeBorder = "D4D4D4";
    public const string InlineCodeBackground = "EEF1F3";
    public const string QuoteRule = "B8BDC2";
    public const string MarkedBackground = "FFF1B8";
    public const string Watermark = "F0F0F0";

    public const string InternalBanner = "FBE9E9";
    public const string InternalText = "8A1C1C";
    public const string ProviderBanner = "FFF4DC";
    public const string ProviderText = "7A5200";
}
