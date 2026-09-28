namespace Overseer.Services.Benchmarking.Pdf;

using System;
using System.Text.RegularExpressions;
using MobileGnollHackLogger.Data;
using Overseer.Models;

/// <summary>
/// The download names of the benchmark PDFs, built exactly as the Download Center builds the names
/// of the Markdown and HTML files, so every format of one document shares one base name.
/// </summary>
public static class BenchmarkPdfFileNames
{
    // JavaScript's \s, spelled out: .NET's \s also matches U+0085 and misses U+FEFF.
    private static readonly Regex JsWhitespace = new(
        "[\t\n\v\f\r \u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000\ufeff]+",
        RegexOptions.CultureInvariant);

    private static readonly Regex Disallowed = new("[^a-z0-9._-]", RegexOptions.CultureInvariant);
    private static readonly Regex Hyphens = new("-{2,}", RegexOptions.CultureInvariant);
    private static readonly Regex Underscores = new("_{2,}", RegexOptions.CultureInvariant);
    private static readonly Regex Dots = new(@"\.{2,}", RegexOptions.CultureInvariant);
    private static readonly Regex EdgeHyphensAndDots = new(@"^[-.]+|[-.]+$", RegexOptions.CultureInvariant);

    /// <summary>
    /// The client's <c>safeFileName</c> (<c>utils/download.util.ts</c>): lowercase, whitespace runs to
    /// hyphens, everything outside <c>[a-z0-9._-]</c> dropped, repeated separators collapsed, leading and
    /// trailing hyphens and dots trimmed, and <c>export</c> when nothing is left.
    /// </summary>
    public static string SafeFileName(string? input)
    {
        string name = (input ?? string.Empty).ToLowerInvariant();
        name = JsWhitespace.Replace(name, "-");
        name = Disallowed.Replace(name, string.Empty);
        name = Hyphens.Replace(name, "-");
        name = Underscores.Replace(name, "_");
        name = Dots.Replace(name, ".");
        name = EdgeHyphensAndDots.Replace(name, string.Empty);
        return name.Length == 0 ? "export" : name;
    }

    /// <summary>
    /// <c>&lt;title&gt;_&lt;disclosure&gt;_&lt;peers&gt;[_INTERNAL].pdf</c>, as the Download Center names a
    /// pack document: the title, else "&lt;audience&gt;: &lt;subject&gt;"; <c>_INTERNAL</c> at Full disclosure.
    /// </summary>
    public static string ForReportDocument(BenchmarkReportDocument document, BenchmarkReportRenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        string title = string.IsNullOrEmpty(document.Title)
            ? BenchmarkReportRenderService.AudienceName(document.Audience) + ": " + document.SubjectLabel
            : document.Title;
        string disclosure = options.Disclosure switch
        {
            BenchmarkReportDisclosure.Detailed => "detailed",
            BenchmarkReportDisclosure.Full => "full",
            _ => "summary"
        };
        string peers = options.PeerNaming == BenchmarkReportPeerNaming.Named ? "named" : "anonymized";
        string internalSuffix = options.Disclosure == BenchmarkReportDisclosure.Full ? "_INTERNAL" : string.Empty;

        return SafeFileName(title) + "_" + disclosure + "_" + peers + internalSuffix + ".pdf";
    }

    /// <summary>A run file's server name with <c>_INTERNAL</c> before a <c>.pdf</c> extension that replaces its own.</summary>
    public static string InternalPdfName(string serverFileName)
    {
        ArgumentNullException.ThrowIfNull(serverFileName);
        int dot = serverFileName.LastIndexOf('.');
        string baseName = dot > 0 ? serverFileName[..dot] : serverFileName;
        return baseName + "_INTERNAL.pdf";
    }
}
