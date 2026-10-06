namespace Overseer.Services.Benchmarking.Pdf;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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

    // A subject that is one battery run: its documents are named from the battery run number first.
    private static readonly Regex BatterySubject = new(@"^battery:([0-9]+)\z", RegexOptions.CultureInvariant);

    // An analysis-group entry, which names a peer but never a document's prefix.
    private static readonly Regex GroupEntry = new(@"^group:([0-9]+)\z", RegexOptions.CultureInvariant);

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
    /// <c>[run-&lt;id&gt;_][vs-&lt;peers&gt;_]&lt;title&gt;_&lt;disclosure&gt;_&lt;peers&gt;[_INTERNAL].&lt;extension&gt;</c>, as
    /// the Download Center names a pack document: <c>run-&lt;id&gt;_</c> when the subject is the run
    /// <c>run:&lt;id&gt;</c>, <c>battery-run-&lt;id&gt;_</c> when it is the battery run <c>battery:&lt;id&gt;</c>
    /// (a group subject has no prefix); the comparison part when the stored fact sheet has N &gt; 0
    /// peers (see <see cref="ComparisonPart"/>); the title, else "&lt;audience&gt;: &lt;subject&gt;";
    /// <c>_INTERNAL</c> at Full disclosure. The extension is <c>pdf</c> or <c>docx</c>, e.g.
    /// <c>run-73_claude-5.5-opus-on-the-gnollbench-executive-summary_full_named_INTERNAL.pdf</c> for a
    /// stand-alone document, <c>battery-run-9_vs-battery-run-10_…_summary_named.pdf</c> for one compared
    /// with one peer and <c>run-68_vs-4-models-3f9a0c21_claude-5.5-opus-…_summary_named.pdf</c> for one
    /// compared with four.
    ///
    /// <para>A Report for AI Researchers and Developers is named
    /// <c>[run-&lt;id&gt;_]&lt;title without its "— &lt;audience name&gt;" ending&gt;_Researcher_Report_…</c>, the
    /// ending being either the current name or the legacy <c>Technical Report</c>; with no title, the
    /// subject label takes the title's place.</para>
    ///
    /// <para>A Report Pack document of a numbered comparison is named by <see cref="ComparisonStem"/>
    /// instead: <c>comparison-12_gpt-5.6-luna-max_executive-summary_full_named_INTERNAL.pdf</c>.</para>
    /// </summary>
    public static string ForReportDocument(
        BenchmarkReportDocument document, BenchmarkReportRenderOptions options, string extension = "pdf")
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        string disclosure = options.Disclosure switch
        {
            BenchmarkReportDisclosure.Detailed => "detailed",
            BenchmarkReportDisclosure.Full => "full",
            _ => "summary"
        };
        string peers = options.PeerNaming == BenchmarkReportPeerNaming.Named ? "named" : "anonymized";
        string internalSuffix = options.Disclosure == BenchmarkReportDisclosure.Full ? "_INTERNAL" : string.Empty;

        if (document.Origin == BenchmarkReportDocumentOrigin.ReportPack
            && BenchmarkPdfDocumentInfo.ComparisonOf(document) is { } comparison)
        {
            return ComparisonStem(document, comparison, options.PeerNaming) + "_" + disclosure + "_" + peers + internalSuffix + "." + extension;
        }

        string title = string.IsNullOrEmpty(document.Title)
            ? BenchmarkReportRenderService.AudienceName(document.Audience) + ": " + document.SubjectLabel
            : document.Title;
        string label = string.Empty;
        if (document.Audience == BenchmarkReportAudience.TechnicalReport)
        {
            title = string.IsNullOrEmpty(document.Title) ? document.SubjectLabel : WithoutAudienceEnding(document.Title);
            label = "_" + ResearcherReportLabel;
        }

        var runSubject = RunSubject.Match(document.SubjectKey ?? string.Empty);
        var batterySubject = BatterySubject.Match(document.SubjectKey ?? string.Empty);
        string runPrefix = runSubject.Success ? "run-" + runSubject.Groups[1].Value + "_"
            : batterySubject.Success ? "battery-run-" + batterySubject.Groups[1].Value + "_"
            : string.Empty;
        string comparisonPrefix = ComparisonPart(document.FactsJson, document.ComparisonKey);

        return runPrefix + comparisonPrefix + SafeFileName(title) + label + "_" + disclosure + "_" + peers + internalSuffix + "." + extension;
    }

    /// <summary>The longest comparison-name or covered-models slug a comparison file name carries.</summary>
    public const int MaxNameSlugLength = 40;

    /// <summary>The most covered models a subset's file name names one by one.</summary>
    private const int MaxNamedCoveredModels = 3;

    /// <summary>
    /// A Report Pack document's stem within its numbered comparison: for model scope
    /// <c>comparison-12_&lt;subject slug&gt;_&lt;kind&gt;</c>; for a document covering every entry
    /// <c>comparison-12_&lt;name slug&gt;_&lt;kind&gt;</c>; for a subset
    /// <c>comparison-12_subset-&lt;covered slug&gt;_&lt;kind&gt;</c>, the covered slug being up to
    /// <see cref="MaxNamedCoveredModels"/> models' slugs joined by <c>-vs-</c> and cut as
    /// <see cref="NameSlug"/> cuts (without a dangling <c>-vs</c>), else <c>3-of-10-models-&lt;first 6 hex of the covered-set key&gt;</c>.
    /// An anonymized copy names no model the comparison name or the covered set would reveal: a
    /// whole-comparison stem leaves the name out and a subset is counted. The kind is <see cref="KindSlug"/>.
    /// </summary>
    public static string ComparisonStem(BenchmarkReportDocument document, BenchmarkPdfComparison comparison, BenchmarkReportPeerNaming naming)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(comparison);

        string prefix = "comparison-" + comparison.Id.ToString(CultureInfo.InvariantCulture);
        string kind = KindSlug(document.Audience);
        bool named = naming == BenchmarkReportPeerNaming.Named;

        string? middle;
        if (document.Scope != BenchmarkReportScope.Comparison)
        {
            middle = SafeFileName(document.SubjectLabel);
        }
        else
        {
            var facts = BenchmarkReportRenderService.ReadFacts(document.FactsJson);
            middle = facts.CoversAllEntries
                ? (named && !string.IsNullOrWhiteSpace(comparison.Name) ? NameSlug(comparison.Name) : null)
                : "subset-" + CoveredSlug(document, comparison, facts, named);
        }

        return middle == null ? prefix + "_" + kind : prefix + "_" + middle + "_" + kind;
    }

    /// <summary>A subset's covered models: their slugs joined by <c>-vs-</c>, or their count with the covered-set key's first 6 hex.</summary>
    private static string CoveredSlug(
        BenchmarkReportDocument document, BenchmarkPdfComparison comparison, BenchmarkReportRenderService.StoredFacts facts, bool named)
    {
        var covered = BenchmarkReportRenderService.CoveredModels(
            BenchmarkReportScope.Comparison, document.SubjectKey, document.SubjectLabel, document.CoveredEntryKeysJson, facts);

        // A model without a label in the sheet carries its entry key as its label and is counted instead.
        if (named && covered.Count is > 0 and <= MaxNamedCoveredModels
            && covered.All(m => !string.Equals(m.Label, m.EntryKey, StringComparison.Ordinal)))
        {
            string joined = CutAtHyphen(string.Join("-vs-", covered.Select(m => SafeFileName(m.Label))), MaxNameSlugLength);
            return joined.EndsWith("-vs", StringComparison.Ordinal) ? joined[..^"-vs".Length] : joined;
        }

        int? total = facts.ComparisonEntryCount ?? comparison.EntryCount;
        string count = covered.Count.ToString(CultureInfo.InvariantCulture)
            + (total is int of ? "-of-" + of.ToString(CultureInfo.InvariantCulture) : string.Empty) + "-models";
        string? setKey = document.CoveredSetKey;
        return setKey is { Length: >= 6 } ? count + "-" + setKey[..6].ToLowerInvariant() : count;
    }

    /// <summary>A comparison's name in a file name: <see cref="SafeFileName"/>, cut at the last hyphen at or before <see cref="MaxNameSlugLength"/> characters.</summary>
    public static string NameSlug(string? name) => CutAtHyphen(SafeFileName(name), MaxNameSlugLength);

    /// <summary>
    /// <paramref name="slug"/> when it fits in <paramref name="max"/> characters, else its part before the last
    /// hyphen that keeps it within them (hard cut where there is none), trailing separators trimmed.
    /// </summary>
    private static string CutAtHyphen(string slug, int max)
    {
        if (slug.Length <= max) return slug;
        int hyphen = slug.LastIndexOf('-', max);
        string cut = (hyphen > 0 ? slug[..hyphen] : slug[..max]).TrimEnd('-', '.', '_');
        return cut.Length == 0 ? slug[..max] : cut;
    }

    /// <summary>A Report Pack document kind in a file name: <c>executive-summary</c>, <c>researcher-report</c> or <c>internal-brief</c>.</summary>
    public static string KindSlug(BenchmarkReportAudience audience) => audience switch
    {
        BenchmarkReportAudience.ExecutiveSummary => "executive-summary",
        BenchmarkReportAudience.TechnicalReport => "researcher-report",
        BenchmarkReportAudience.InternalBrief => "internal-brief",
        _ => SafeFileName(audience.ToString())
    };

    /// <summary>The most peers a file name lists one by one.</summary>
    private const int MaxNamedPeers = 3;

    /// <summary>
    /// The part of a pack document's name that keeps one comparison's documents apart from another's,
    /// with its trailing <c>_</c>; empty for a stand-alone document (no peers, or a fact sheet that
    /// cannot be read). With 1 to <see cref="MaxNamedPeers"/> peers whose entry keys are all readable,
    /// <c>vs-</c> and their tokens in letter order joined by <c>-</c>, a token being <c>run-&lt;id&gt;</c>,
    /// <c>group-&lt;id&gt;</c> or <c>battery-run-&lt;id&gt;</c> (<c>run-92_vs-run-94-run-95_</c>); otherwise
    /// <c>vs-&lt;N&gt;-models-&lt;first 8 characters of the comparison key&gt;_</c>, or
    /// <c>vs-&lt;N&gt;-models_</c> for a row stored without a comparison key.
    /// </summary>
    private static string ComparisonPart(string? factsJson, string? comparisonKey)
    {
        var (count, peers) = PeersOf(factsJson);
        if (count == 0) return string.Empty;

        if (count <= MaxNamedPeers && peers.Count == count)
        {
            var tokens = new List<string>();
            foreach (var (_, entryKey) in peers.OrderBy(p => p.Letter.Length).ThenBy(p => p.Letter, StringComparer.Ordinal))
            {
                string? token = EntryToken(entryKey);
                if (token == null) break;
                tokens.Add(token);
            }
            if (tokens.Count == count) return "vs-" + string.Join("-", tokens) + "_";
        }

        string models = "vs-" + count.ToString(CultureInfo.InvariantCulture) + "-models";
        return string.IsNullOrEmpty(comparisonKey)
            ? models + "_"
            : models + "-" + (comparisonKey.Length > 8 ? comparisonKey[..8] : comparisonKey) + "_";
    }

    /// <summary>The file-name token of a comparison entry key; null for any other form.</summary>
    private static string? EntryToken(string entryKey)
    {
        var run = RunSubject.Match(entryKey);
        if (run.Success) return "run-" + run.Groups[1].Value;
        var group = GroupEntry.Match(entryKey);
        if (group.Success) return "group-" + group.Groups[1].Value;
        var battery = BatterySubject.Match(entryKey);
        if (battery.Success) return "battery-run-" + battery.Groups[1].Value;
        return null;
    }

    /// <summary>
    /// The length of the stored fact sheet's <c>peers</c> array, as the document list reports it to the
    /// client, and the letter and entry key of each peer that has both; 0 and none when absent or
    /// unreadable.
    /// </summary>
    private static (int Count, List<(string Letter, string EntryKey)> Peers) PeersOf(string? factsJson)
    {
        var none = (0, new List<(string Letter, string EntryKey)>());
        if (string.IsNullOrWhiteSpace(factsJson)) return none;
        try
        {
            using var doc = JsonDocument.Parse(factsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return none;
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, "peers", StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.Array)
                {
                    var peers = new List<(string Letter, string EntryKey)>();
                    foreach (var peer in property.Value.EnumerateArray())
                    {
                        string? letter = StringProperty(peer, "letter");
                        string? entryKey = StringProperty(peer, "entryKey");
                        if (!string.IsNullOrEmpty(letter) && !string.IsNullOrEmpty(entryKey)) peers.Add((letter, entryKey));
                    }
                    return (property.Value.GetArrayLength(), peers);
                }
            }
            return none;
        }
        catch (JsonException)
        {
            return none;
        }
    }

    /// <summary>A string property of a JSON object, matched by name ignoring case; null when absent or not a string.</summary>
    private static string? StringProperty(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
            }
        }
        return null;
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
