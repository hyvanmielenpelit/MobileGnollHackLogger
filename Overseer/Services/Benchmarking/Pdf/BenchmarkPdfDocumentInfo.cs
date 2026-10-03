namespace Overseer.Services.Benchmarking.Pdf;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MobileGnollHackLogger.Data;
using Overseer.Models;

/// <summary>Who a PDF may be shown to; the classification banner, footer and watermark follow it.</summary>
public enum BenchmarkPdfClassification
{
    /// <summary>Overseer team only: a pack document at Full disclosure and every run file.</summary>
    Internal = 1,

    /// <summary>A pack document at Summary or Detailed disclosure, prepared for the model's provider.</summary>
    ProviderConfidential = 2,
}

public enum BenchmarkPdfPaper
{
    A4 = 1,
    Letter = 2,
}

/// <summary>One label and value of a PDF's title-block facts table.</summary>
public sealed record BenchmarkPdfFact(string Label, string Value);

/// <summary>
/// Everything a benchmark PDF prints or embeds besides its source text: the title block, the
/// classification, the metadata and the paper. Built from stored rows only, never from the request,
/// so the same row renders the same PDF content whenever it is downloaded.
/// </summary>
public sealed record BenchmarkPdfDocumentInfo
{
    /// <summary>The stamp of the tool-call log and the run diagnostics.</summary>
    public const string TeamOnlyStamp = "INTERNAL — Overseer team only.";

    public const string PaperError = "paper must be a4 or letter.";

    /// <summary>Executive Summary, Report for AI Researchers and Developers, Internal Improvement Brief, Run report, Tool-call log or Run diagnostics.</summary>
    public required string DocumentKind { get; init; }

    public required string Title { get; init; }

    public required string SubjectLine { get; init; }

    public required BenchmarkPdfClassification Classification { get; init; }

    /// <summary>The stamp printed in the classification banner on page 1.</summary>
    public required string ClassificationText { get; init; }

    /// <summary>The title block's facts table, in order.</summary>
    public IReadOnlyList<BenchmarkPdfFact> Facts { get; init; } = Array.Empty<BenchmarkPdfFact>();

    /// <summary>The PDF's creation and modification date, from the stored row; never the request time.</summary>
    public required DateTime CreatedAtUtc { get; init; }

    public IReadOnlyList<string> Keywords { get; init; } = Array.Empty<string>();

    public BenchmarkPdfPaper Paper { get; init; } = BenchmarkPdfPaper.A4;

    /// <summary>
    /// SHA-256 of the UTF-8 source text, lowercase hex. The renderer sets it from the source it is
    /// given; its first 16 characters are printed in the title block and the footer.
    /// </summary>
    public string SourceSha256 { get; init; } = string.Empty;

    /// <summary>
    /// Whether a table of contents may follow the title block. It is printed only when the document
    /// also has four or more <c>##</c> sections; the Executive Summary never gets one.
    /// </summary>
    public bool AllowTableOfContents { get; init; } = true;

    /// <summary>The short classification printed at the left of every footer.</summary>
    public string FooterClassification => Classification == BenchmarkPdfClassification.Internal
        ? "INTERNAL"
        : "CONFIDENTIAL — PROVIDER COPY";

    /// <summary><c>a4</c> or <c>letter</c>, case-insensitive; empty means A4.</summary>
    public static bool TryParsePaper(string? value, out BenchmarkPdfPaper paper)
    {
        paper = BenchmarkPdfPaper.A4;
        if (string.IsNullOrWhiteSpace(value)) return true;
        switch (value.Trim().ToLowerInvariant())
        {
            case "a4":
                return true;
            case "letter":
                paper = BenchmarkPdfPaper.Letter;
                return true;
            default:
                return false;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Builders, one per downloadable document
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A stored report-pack document rendered at the given disclosure and peer naming. A document with
    /// peers says so in its subject line, and its facts table names them under Compared with, as the
    /// naming allows, and states the pricing basis. A battery document's subject line names the
    /// battery run (<see cref="BatterySubjectLine"/>), and its facts table the battery, the battery run
    /// and the count of member runs in place of the suite and the runs.
    /// </summary>
    public static BenchmarkPdfDocumentInfo ForReportDocument(
        BenchmarkReportDocument document, BenchmarkReportRenderOptions options, BenchmarkPdfPaper paper)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        string audience = BenchmarkReportRenderService.AudienceName(document.Audience);
        bool full = options.Disclosure == BenchmarkReportDisclosure.Full;
        var runIds = ParseRunIds(document.SubjectRunIdsJson);
        var sheet = ParseSheet(document.FactsJson);
        bool compared = sheet != null && sheet.Peers.Count > 0;

        var battery = sheet?.Battery;

        var facts = new List<BenchmarkPdfFact>
        {
            new("Document ID", Inv(document.Id)),
            new("Disclosure", options.Disclosure.ToString()),
        };
        if (compared)
        {
            facts.Add(new("Compared with", ComparedWithText(sheet!, options.PeerNaming)));
            facts.Add(new("Pricing basis", BenchmarkReportPackRenderer.PricingBasisSummary(sheet!)));
        }
        else
        {
            facts.Add(new("Peers", PeersText(sheet, options.PeerNaming)));
        }
        if (battery != null)
        {
            // A battery's member runs can number in the hundreds; the cover counts them.
            facts.AddRange(new BenchmarkPdfFact[]
            {
                new("Battery", BatteryText(battery)),
                new("Questions", QuestionsText(sheet)),
                new("Battery run", "#" + Inv(battery.BatteryRunId)),
                new("Member runs", Inv(runIds.Count)),
            });
        }
        else
        {
            facts.AddRange(new BenchmarkPdfFact[]
            {
                new("Suite", document.SuiteName ?? string.Empty),
                new("Questions", QuestionsText(sheet)),
                new(runIds.Count == 1 ? "Run" : "Runs", runIds.Count == 0 ? "—" : string.Join(", ", runIds.Select(id => "#" + Inv(id)))),
            });
        }
        facts.AddRange(new BenchmarkPdfFact[]
        {
            new("Created (UTC)", Stamp(document.CreatedAtUtc)),
            new("Generated format", "version " + Inv(document.ReportFormatVersion)),
            new("Writer", WriterText(document)),
            new("Provenance", ProvenanceText),
        });

        string subjectLine = battery != null
            ? BatterySubjectLine(battery) + (compared ? " · compared with " + ModelCount(sheet!.Peers.Count) : string.Empty)
            : compared
                ? ComparisonSubjectLine(document.SuiteName, document.SubjectKey, runIds, sheet!.Peers.Count)
                : SubjectLineOf(document.SuiteName, runIds);

        return new BenchmarkPdfDocumentInfo
        {
            DocumentKind = audience,
            Title = string.IsNullOrWhiteSpace(document.Title)
                ? audience + ": " + document.SubjectLabel
                : BenchmarkReportRenderService.CurrentTitle(document.Audience, document.Title),
            SubjectLine = subjectLine,
            Classification = full ? BenchmarkPdfClassification.Internal : BenchmarkPdfClassification.ProviderConfidential,
            ClassificationText = BenchmarkReportPackRenderer.Stamp(document.Audience, options.Disclosure),
            Facts = facts,
            CreatedAtUtc = document.CreatedAtUtc,
            Keywords = KeywordsOf(document.SubjectLabel, document.SuiteName, audience),
            Paper = paper,
            AllowTableOfContents = document.Audience != BenchmarkReportAudience.ExecutiveSummary
        };
    }

    /// <summary>A run's Markdown report.</summary>
    public static BenchmarkPdfDocumentInfo ForRunReport(BenchmarkRun run, string? overseerVersion, BenchmarkPdfPaper paper)
    {
        ArgumentNullException.ThrowIfNull(run);
        string model = ModelLabel(run);
        var facts = RunFacts(run);
        facts.Add(new("Overseer version", string.IsNullOrWhiteSpace(overseerVersion) ? "not recorded" : overseerVersion));

        return new BenchmarkPdfDocumentInfo
        {
            DocumentKind = "Run report",
            Title = "Run report: " + model,
            SubjectLine = SubjectLineOf(run.SuiteName, new[] { run.Id }),
            Classification = BenchmarkPdfClassification.Internal,
            ClassificationText = BenchmarkReportPackRenderer.Stamp(BenchmarkReportDisclosure.Full),
            Facts = facts,
            CreatedAtUtc = run.CompletedAtUtc ?? run.StartedAtUtc,
            Keywords = KeywordsOf(model, run.SuiteName, "Run report"),
            Paper = paper
        };
    }

    /// <summary>A run's tool-call log.</summary>
    public static BenchmarkPdfDocumentInfo ForToolCallLog(BenchmarkRun run, BenchmarkPdfPaper paper)
    {
        ArgumentNullException.ThrowIfNull(run);
        string model = ModelLabel(run);

        return new BenchmarkPdfDocumentInfo
        {
            DocumentKind = "Tool-call log",
            Title = "Tool-call log: " + model,
            SubjectLine = SubjectLineOf(run.SuiteName, new[] { run.Id }),
            Classification = BenchmarkPdfClassification.Internal,
            ClassificationText = TeamOnlyStamp,
            Facts = RunFacts(run),
            CreatedAtUtc = run.CompletedAtUtc ?? run.StartedAtUtc,
            Keywords = KeywordsOf(model, run.SuiteName, "Tool-call log"),
            Paper = paper
        };
    }

    /// <summary>A run's diagnostics text, as the client captured it at <paramref name="capturedAtUtc"/>.</summary>
    public static BenchmarkPdfDocumentInfo ForDiagnostics(
        BenchmarkRun run, DateTime capturedAtUtc, string? overseerVersion, BenchmarkPdfPaper paper)
    {
        ArgumentNullException.ThrowIfNull(run);
        string model = ModelLabel(run);
        var facts = new List<BenchmarkPdfFact>
        {
            new("Run", "#" + Inv(run.Id)),
            new("Suite", run.SuiteName ?? string.Empty),
            new("Model", model),
            new("Captured (UTC)", Stamp(capturedAtUtc)),
            new("Overseer version", string.IsNullOrWhiteSpace(overseerVersion) ? "not recorded" : overseerVersion),
        };

        return new BenchmarkPdfDocumentInfo
        {
            DocumentKind = "Run diagnostics",
            Title = "Run diagnostics: " + model,
            SubjectLine = SubjectLineOf(run.SuiteName, new[] { run.Id }),
            Classification = BenchmarkPdfClassification.Internal,
            ClassificationText = TeamOnlyStamp,
            Facts = facts,
            CreatedAtUtc = capturedAtUtc,
            Keywords = KeywordsOf(model, run.SuiteName, "Run diagnostics"),
            Paper = paper,
            AllowTableOfContents = false
        };
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------

    private static List<BenchmarkPdfFact> RunFacts(BenchmarkRun run)
    {
        var snapshot = run.TestedModelSnapshot;
        string model = ModelLabel(run);
        if (snapshot != null && !string.IsNullOrWhiteSpace(snapshot.ModelId))
        {
            model += " (" + snapshot.Provider + " / " + snapshot.ModelId + ")";
        }

        var facts = new List<BenchmarkPdfFact>
        {
            new("Run", "#" + Inv(run.Id)),
            new("Suite", run.SuiteName ?? string.Empty),
            new("Model", model),
            new("Harness version", run.HarnessVersion ?? "1 (unversioned legacy)"),
            new("Started (UTC)", Stamp(run.StartedAtUtc)),
        };
        if (run.CompletedAtUtc != null)
        {
            facts.Add(new("Completed (UTC)", Stamp(run.CompletedAtUtc.Value)));
        }
        return facts;
    }

    private static string ModelLabel(BenchmarkRun run) => run.TestedModelSnapshot.Label() ?? "unknown model";

    /// <summary>The document's stored fact sheet; null when it cannot be read.</summary>
    private static BenchmarkReportFactSheet? ParseSheet(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(json);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>"none (stand-alone report)", or the peer count and how they are named; the naming alone without a sheet.</summary>
    private static string PeersText(BenchmarkReportFactSheet? sheet, BenchmarkReportPeerNaming naming)
    {
        string mode = naming == BenchmarkReportPeerNaming.Named ? "named" : "anonymized";
        if (sheet == null) return naming == BenchmarkReportPeerNaming.Named ? "Named" : "Anonymized";
        if (sheet.Peers.Count == 0) return "none (stand-alone report)";
        return Inv(sheet.Peers.Count) + ", " + mode;
    }

    /// <summary>
    /// The peers of a comparison: named, their labels in letter order and the count, <c>Grok 5 and
    /// Mistral Large 4 (2 models)</c>; anonymized, the count and letters, <c>4 models (A to D),
    /// identities withheld</c>.
    /// </summary>
    private static string ComparedWithText(BenchmarkReportFactSheet sheet, BenchmarkReportPeerNaming naming)
    {
        var peers = sheet.Peers.OrderBy(p => p.Letter.Length).ThenBy(p => p.Letter, StringComparer.Ordinal).ToList();
        string count = ModelCount(peers.Count);

        if (naming == BenchmarkReportPeerNaming.Named)
        {
            return BenchmarkReportFormat.LetterList(peers.Select(p => p.Label).ToList()) + " (" + count + ")";
        }

        string letters = peers.Count switch
        {
            1 => peers[0].Letter,
            2 => peers[0].Letter + " and " + peers[1].Letter,
            _ => peers[0].Letter + " to " + peers[^1].Letter
        };
        return count + " (" + letters + "), " + (peers.Count == 1 ? "identity withheld" : "identities withheld");
    }

    /// <summary>
    /// <c>Suite · run #68 · compared with 4 models</c>; <c>group #5</c> in place of the runs for a group
    /// subject.
    /// </summary>
    private static string ComparisonSubjectLine(string? suiteName, string? subjectKey, IReadOnlyCollection<long> runIds, int peerCount)
    {
        const string groupPrefix = "group:";
        string key = subjectKey ?? string.Empty;
        string subject = key.StartsWith(groupPrefix, StringComparison.Ordinal) && key.Length > groupPrefix.Length
                         && key[groupPrefix.Length..].All(char.IsAsciiDigit)
            ? "group #" + key[groupPrefix.Length..]
            : SubjectLineOf(null, runIds);

        return string.Join(" · ", new[] { suiteName, subject, "compared with " + ModelCount(peerCount) }
            .Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    private static string ModelCount(int count) => Inv(count) + (count == 1 ? " model" : " models");

    /// <summary><c>Battery run #9 — Core knowledge (12 suites, 10 runs per suite)</c>.</summary>
    public static string BatterySubjectLine(BenchmarkReportBatterySubject battery)
    {
        ArgumentNullException.ThrowIfNull(battery);
        return "Battery run #" + Inv(battery.BatteryRunId) + " — " + battery.Name
            + " (" + Inv(battery.SuiteCount) + (battery.SuiteCount == 1 ? " suite, " : " suites, ")
            + Inv(battery.RunsPerSuite) + (battery.RunsPerSuite == 1 ? " run per suite)" : " runs per suite)");
    }

    /// <summary><c>Core knowledge, revision 3 (12 suites)</c>.</summary>
    private static string BatteryText(BenchmarkReportBatterySubject battery)
        => battery.Name
           + (battery.Revision is int revision ? ", revision " + Inv(revision) : string.Empty)
           + " (" + Inv(battery.SuiteCount) + (battery.SuiteCount == 1 ? " suite)" : " suites)");

    /// <summary>The questions of the subject's exam, from the sheet's <c>suite.questions</c>, else its question count.</summary>
    private static string QuestionsText(BenchmarkReportFactSheet? sheet)
    {
        if (sheet == null) return "—";
        var fact = sheet.Facts.FirstOrDefault(f => string.Equals(f.Key, "suite.questions", StringComparison.Ordinal));
        return fact is { Available: true } ? fact.Display : Inv(sheet.Questions.Count);
    }

    /// <summary>What a report-pack document's figures and prose rest on, and what the automatic checks do not cover.</summary>
    private const string ProvenanceText = "Figures and tables computed by Overseer; prose written by the writer and checked "
        + "automatically for structure, permitted figures and references, word limits and disclosure. The checks do not "
        + "verify its interpretations.";

    /// <summary><c>display name (provider, model id; thinking level)</c>, each empty part left out.</summary>
    private static string WriterText(BenchmarkReportDocument document)
    {
        string writer = string.IsNullOrWhiteSpace(document.WriterDisplayName) ? document.WriterModelId : document.WriterDisplayName;

        string identity = string.Join(", ", new[] { document.WriterProvider, document.WriterModelId }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim()));
        string details = string.Join("; ", new[] { identity, document.WriterThinkingLevel?.Trim() ?? string.Empty }
            .Where(p => p.Length > 0));

        return details.Length == 0 ? writer : writer + " (" + details + ")";
    }

    private static string SubjectLineOf(string? suiteName, IReadOnlyCollection<long> runIds)
    {
        string runs = runIds.Count == 0
            ? string.Empty
            : (runIds.Count == 1 ? "run " : "runs ") + string.Join(", ", runIds.Select(id => "#" + Inv(id)));
        if (string.IsNullOrWhiteSpace(suiteName)) return runs;
        return runs.Length == 0 ? suiteName : suiteName + " · " + runs;
    }

    private static IReadOnlyList<string> KeywordsOf(string? model, string? suite, string kind)
        => new[] { model, suite, kind, "GnollBench" }
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k!)
            .ToList();

    private static List<long> ParseRunIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<long>();
        try
        {
            return BenchmarkReportJson.Deserialize<List<long>>(json) ?? new List<long>();
        }
        catch (Exception)
        {
            return new List<long>();
        }
    }

    private static string Stamp(DateTime utc)
        => utc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static string Inv(long value) => value.ToString(CultureInfo.InvariantCulture);
}
