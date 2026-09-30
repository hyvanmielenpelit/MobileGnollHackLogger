namespace Overseer.Services.Benchmarking.Pdf;

using System;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using MobileGnollHackLogger.Data;
using Overseer.Models;

/// <summary>
/// The download names of the benchmark PDFs and Word documents, built exactly as the Download Center builds the names
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

    // A subject that is one run: its documents are named from the run number first.
    private static readonly Regex RunSubject = new(@"^run:([0-9]+)\z", RegexOptions.CultureInvariant);

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

    /// <summary>The file-name label of a Report for AI Researchers and Developers, in place of its long name.</summary>
    public const string ResearcherReportLabel = "Researcher_Report";

    /// <summary>
    /// <c>[run-&lt;id&gt;_][vs-&lt;N&gt;-models_]&lt;title&gt;_&lt;disclosure&gt;_&lt;peers&gt;[_INTERNAL].&lt;extension&gt;</c>, as
    /// the Download Center names a pack document: <c>run-&lt;id&gt;_</c> when the subject is the run
    /// <c>run:&lt;id&gt;</c> (a group subject has no prefix); <c>vs-&lt;N&gt;-models_</c> when the stored fact
    /// sheet has N &gt; 0 peers, whatever N is; the title, else "&lt;audience&gt;: &lt;subject&gt;";
    /// <c>_INTERNAL</c> at Full disclosure. The extension is <c>pdf</c> or <c>docx</c>, e.g.
    /// <c>run-73_claude-5.5-opus-on-the-gnollbench-executive-summary_full_named_INTERNAL.pdf</c> for a
    /// stand-alone document and <c>run-68_vs-4-models_claude-5.5-opus-…_summary_named.pdf</c> for one
    /// compared with four peers.
    ///
    /// <para>A Report for AI Researchers and Developers is named
    /// <c>[run-&lt;id&gt;_]&lt;title without its "— &lt;audience name&gt;" ending&gt;_Researcher_Report_…</c>, the
    /// ending being either the current name or the legacy <c>Technical Report</c>; with no title, the
    /// subject label takes the title's place.</para>
    /// </summary>
    public static string ForReportDocument(
        BenchmarkReportDocument document, BenchmarkReportRenderOptions options, string extension = "pdf")
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        string title = string.IsNullOrEmpty(document.Title)
            ? BenchmarkReportRenderService.AudienceName(document.Audience) + ": " + document.SubjectLabel
            : document.Title;
        string label = string.Empty;
        if (document.Audience == BenchmarkReportAudience.TechnicalReport)
        {
            title = string.IsNullOrEmpty(document.Title) ? document.SubjectLabel : WithoutAudienceEnding(document.Title);
            label = "_" + ResearcherReportLabel;
        }
        string disclosure = options.Disclosure switch
        {
            BenchmarkReportDisclosure.Detailed => "detailed",
            BenchmarkReportDisclosure.Full => "full",
            _ => "summary"
        };
        string peers = options.PeerNaming == BenchmarkReportPeerNaming.Named ? "named" : "anonymized";
        string internalSuffix = options.Disclosure == BenchmarkReportDisclosure.Full ? "_INTERNAL" : string.Empty;

        var runSubject = RunSubject.Match(document.SubjectKey ?? string.Empty);
        string runPrefix = runSubject.Success ? "run-" + runSubject.Groups[1].Value + "_" : string.Empty;
        int peerCount = PeerCountOf(document.FactsJson);
        string comparisonPrefix = peerCount > 0 ? "vs-" + peerCount.ToString(CultureInfo.InvariantCulture) + "-models_" : string.Empty;

        return runPrefix + comparisonPrefix + SafeFileName(title) + label + "_" + disclosure + "_" + peers + internalSuffix + "." + extension;
    }

    /// <summary>
    /// The length of the stored fact sheet's <c>peers</c> array, as the document list reports it to the
    /// client; 0 when absent or unreadable.
    /// </summary>
    private static int PeerCountOf(string? factsJson)
    {
        if (string.IsNullOrWhiteSpace(factsJson)) return 0;
        try
        {
            using var doc = JsonDocument.Parse(factsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return 0;
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "peers", StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.Array)
                {
                    return property.Value.GetArrayLength();
                }
            }
            return 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    /// <summary>The title without a trailing <c> — Report for AI Researchers and Developers</c> or <c> — Technical Report</c>.</summary>
    private static string WithoutAudienceEnding(string title)
    {
        foreach (string name in new[]
                 {
                     BenchmarkReportRenderService.AudienceName(BenchmarkReportAudience.TechnicalReport),
                     BenchmarkReportRenderService.LegacyTechnicalReportName
                 })
        {
            string ending = " — " + name;
            if (title.EndsWith(ending, StringComparison.Ordinal)) return title[..^ending.Length];
        }
        return title;
    }

    /// <summary>A run file's server name with <c>_INTERNAL</c> before a <c>.pdf</c> extension that replaces its own.</summary>
    public static string InternalPdfName(string serverFileName) => InternalFileName(serverFileName, "pdf");

    /// <summary>A run file's server name with <c>_INTERNAL</c> before an extension that replaces its own.</summary>
    public static string InternalFileName(string serverFileName, string extension)
    {
        ArgumentNullException.ThrowIfNull(serverFileName);
        ArgumentNullException.ThrowIfNull(extension);
        int dot = serverFileName.LastIndexOf('.');
        string baseName = dot > 0 ? serverFileName[..dot] : serverFileName;
        return baseName + "_INTERNAL." + extension;
    }
}
