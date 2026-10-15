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

    // Callouts: a rule in the kind's color down the left of a light tint. The text on the tint is ink,
    // at least 4.5 : 1 against every tint; the rule is at least 3 : 1 against its tint and white.
    public const string CalloutNoteRule = "2F6FB5";
    public const string CalloutNoteTint = "EAF2FB";
    public const string CalloutImportantRule = "7048B8";
    public const string CalloutImportantTint = "F2EDFA";
    public const string CalloutTipRule = "2B7A4B";
    public const string CalloutTipTint = "E8F4EC";
    public const string CalloutWarningRule = "9A6200";
    public const string CalloutWarningTint = "FFF4D6";

    /// <summary>A callout kind's rule color and tint.</summary>
    public static (string Rule, string Tint) Callout(BenchmarkCalloutKind kind) => kind switch
    {
        BenchmarkCalloutKind.Important => (CalloutImportantRule, CalloutImportantTint),
        BenchmarkCalloutKind.Tip => (CalloutTipRule, CalloutTipTint),
        BenchmarkCalloutKind.Warning => (CalloutWarningRule, CalloutWarningTint),
        _ => (CalloutNoteRule, CalloutNoteTint)
    };
}
