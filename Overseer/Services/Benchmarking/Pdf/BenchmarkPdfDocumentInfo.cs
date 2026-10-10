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
/// The numbered comparison a Report Pack document belongs to, as its cover, running header, Markdown
/// and file name print it: the number, and the display name and entry count when they are known.
/// </summary>
public sealed record BenchmarkPdfComparison(int Id, string? Name, int? EntryCount);

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

    /// <summary>
    /// The running header's right-hand text when it is not the subject line: a Report Pack document's
    /// <c>Comparison #12 — name</c>, cut with an ellipsis at the header's width. Null prints the subject line whole.
    /// </summary>
    public string? HeaderText { get; init; }

    /// <summary>What the running header prints at its right: <see cref="HeaderText"/>, else <see cref="SubjectLine"/>.</summary>
    public string RunningHeaderText => string.IsNullOrWhiteSpace(HeaderText) ? SubjectLine : HeaderText;

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
    /// What the footer prints before the page number, in place of the source hash and layout version,
    /// and the title block omits the source hash for: a chat consistency document's
    /// <c>Chat consistency analysis #4 · Executive Summary</c>. Null prints the source hash and layout version.
    /// </summary>
    public string? FooterText { get; init; }

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
    ///
    /// <para>A document of a numbered comparison (<see cref="ComparisonOf"/>, from
    /// <see cref="BenchmarkReportDocument.Comparison"/> when it is loaded) opens its facts table with a
    /// Comparison row (<see cref="ComparisonText"/>), prints <see cref="ComparisonHeading"/> in the
    /// running header, and states the comparison in its subject line: after today's line for model
    /// scope, in its place for comparison scope (<see cref="ComparisonScopeSubjectLine"/>).</para>
    ///
    /// <para>A comparison-scope document's facts table names its covered models in place of Compared with
    /// (<see cref="ComparisonScopeFacts"/>), and its title is built for the naming
    /// (<see cref="BenchmarkReportPackRenderer.BuildComparisonTitle"/>), since an anonymized copy never
    /// prints the comparison's name.</para>
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
        if (sheet?.IsChatConsistency == true)
        {
            return ForChatConsistencyDocument(document, sheet, options, paper);
        }
        bool compared = sheet != null && sheet.Peers.Count > 0;
        var comparison = ComparisonOf(document);
        bool comparisonScope = sheet?.IsComparison == true;

        var battery = sheet?.Battery;

        var facts = new List<BenchmarkPdfFact>();
        if (comparison is { } numbered)
        {
            facts.Add(new("Comparison", ComparisonText(numbered, ComparisonComputedOn(sheet), options.PeerNaming)));
        }
        facts.AddRange(new BenchmarkPdfFact[]
        {
            new("Document ID", Inv(document.Id)),
            new("Disclosure", options.Disclosure.ToString()),
        });
        if (comparisonScope)
        {
            facts.AddRange(ComparisonScopeFacts(document, sheet!, comparison, runIds, options.PeerNaming));
        }
        else if (compared)
        {
            facts.Add(new("Compared with", ComparedWithText(sheet!, options.PeerNaming)));
            facts.Add(new("Pricing basis", BenchmarkReportPackRenderer.PricingBasisSummary(sheet!)));
        }
        else
        {
            facts.Add(new("Peers", PeersText(sheet, options.PeerNaming)));
        }
        if (!comparisonScope && battery != null)
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
        else if (!comparisonScope)
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

        string subjectLine = comparisonScope
            ? string.Join(" · ", new[] { sheet!.SubjectLabel, document.SuiteName }.Where(p => !string.IsNullOrWhiteSpace(p)))
            : battery != null
                ? BatterySubjectLine(battery) + (compared ? " · compared with " + ModelCount(sheet!.Peers.Count) : string.Empty)
                : compared
                    ? ComparisonSubjectLine(document.SuiteName, document.SubjectKey, runIds, sheet!.Peers.Count)
                    : SubjectLineOf(document.SuiteName, runIds);
        if (comparison is { } numberedComparison)
        {
            subjectLine = document.Scope == BenchmarkReportScope.Comparison
                ? ComparisonScopeSubjectLine(document, numberedComparison, options.PeerNaming)
                : string.Join(" · ", new[] { subjectLine, "Comparison #" + Inv(numberedComparison.Id) }.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        return new BenchmarkPdfDocumentInfo
        {
            DocumentKind = audience,
            Title = comparisonScope
                ? BenchmarkReportPackRenderer.BuildComparisonTitle(document.Audience, sheet!, comparison, options.PeerNaming)
                : string.IsNullOrWhiteSpace(document.Title)
                    ? audience + ": " + document.SubjectLabel
                    : BenchmarkReportRenderService.CurrentTitle(document.Audience, document.Title),
            SubjectLine = subjectLine,
            HeaderText = comparison is { } headerComparison ? ComparisonHeading(headerComparison, options.PeerNaming) : null,
            Classification = full ? BenchmarkPdfClassification.Internal : BenchmarkPdfClassification.ProviderConfidential,
            ClassificationText = BenchmarkReportPackRenderer.Stamp(document.Audience, options.Disclosure),
            Facts = facts,
            CreatedAtUtc = document.CreatedAtUtc,
            Keywords = KeywordsOf(document.SubjectLabel, document.SuiteName, audience),
            Paper = paper,
            AllowTableOfContents = document.Audience != BenchmarkReportAudience.ExecutiveSummary
        };
    }

    /// <summary>
    /// A stored chat consistency document: its cover names the model, the battery or suite compared,
    /// both periods with their battery runs or runs, the control models as the naming allows (never by
    /// name in a Provider Issue Report), the hours, the protocol, the analysis code version (out of date
    /// as decided when rendered), who wrote it when and the provenance. The subject line is <c>Chat consistency
    /// analysis #12 — name</c>, the name left out of an anonymized copy and when it is the default name, and the footer
    /// <c>Chat consistency analysis #12 · Executive Summary</c> before the page number: no hash anywhere.
    /// </summary>
    private static BenchmarkPdfDocumentInfo ForChatConsistencyDocument(
        BenchmarkReportDocument document, BenchmarkReportFactSheet sheet, BenchmarkReportRenderOptions options, BenchmarkPdfPaper paper)
    {
        string audience = BenchmarkReportRenderService.AudienceName(document.Audience);
        var naming = document.Audience == BenchmarkReportAudience.ProviderIssueReport ? BenchmarkReportPeerNaming.Anonymized : options.PeerNaming;
        var subject = sheet.ChatConsistency ?? new BenchmarkReportChatConsistencySubject();
        BenchmarkReportFact? FactOf(string key) => sheet.Facts.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.Ordinal));
        string Fact(string key) => FactOf(key) is { Available: true } fact ? fact.Display : "—";
        string Period(string period)
        {
            string p = "period." + period + ".";
            return Fact(p + "start") + " to " + Fact(p + "end") + ", " + (FactOf(p + "units") is { Available: true } ? Fact(p + "units") : Fact(p + "runs"));
        }

        string analysis = "Chat consistency analysis " + (subject.AnalysisId is int id ? "#" + Inv(id) : "(not saved)");
        string subjectLine = naming == BenchmarkReportPeerNaming.Named && !string.IsNullOrWhiteSpace(subject.Name) && !IsDefaultChatName(subject.Name, sheet)
            ? analysis + " — " + subject.Name.Trim()
            : analysis;

        int codeVersion = subject.AnalysisCodeVersion > 0
            ? subject.AnalysisCodeVersion
            : FactOf("analysis.codeVersion") is { Available: true, Value: System.Text.Json.Nodes.JsonValue v } && v.TryGetValue(out int stored) ? stored : 0;
        int current = Overseer.Services.ChatConsistency.ChatConsistencyAnalysisService.CurrentAnalysisCodeVersion;
        string analysisCode = codeVersion <= 0
            ? "not recorded"
            : codeVersion < current
                ? "version " + Inv(codeVersion) + " — out of date (current: " + Inv(current) + ")"
                : FactOf("analysis.writtenOutOfDate") is { Available: true } written && written.Display.Contains("changed inputs", StringComparison.Ordinal)
                    ? "version " + Inv(codeVersion) + " — out of date (its inputs changed)"
                    : "version " + Inv(codeVersion);
        string protocolLabel = !string.IsNullOrWhiteSpace(subject.ProtocolLabel) ? subject.ProtocolLabel.Trim() : Fact("protocol.label");
        string protocol = protocolLabel + ", α " + Fact("protocol.alpha");

        string model = string.Join(", ", new[] { sheet.SubjectProvider, sheet.SubjectModelId }.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()));
        string compared = FactOf("analysis.compared") is { Available: true }
            ? Fact("analysis.compared")
            : string.IsNullOrWhiteSpace(sheet.SuiteName) ? "—" : sheet.SuiteName.Trim();
        var facts = new List<BenchmarkPdfFact>
        {
            new("Model", model.Length == 0 ? sheet.SubjectLabel : sheet.SubjectLabel + " (" + model + ")"),
            new("Compared", compared),
            new("Baseline", Period("baseline")),
            new("Comparison", Period("comparison")),
            new("Controls", sheet.Peers.Count == 0 ? "none" : ComparedWithText(sheet, naming)),
            new("Hours", FactOf("scope.hours") is { Available: true } ? Fact("scope.hours") : "none: the periods ran at different hours"),
            new("Protocol", protocol),
            new("Analysis code", analysisCode),
            new("Written", Stamp(document.CreatedAtUtc) + " UTC by " + WriterText(document)),
            new("Provenance", ProvenanceText),
        };

        return new BenchmarkPdfDocumentInfo
        {
            DocumentKind = audience,
            Title = string.IsNullOrWhiteSpace(document.Title) ? BenchmarkReportPackRenderer.BuildChatConsistencyTitle(sheet) : document.Title,
            SubjectLine = subjectLine,
            Classification = options.Disclosure == BenchmarkReportDisclosure.Full ? BenchmarkPdfClassification.Internal : BenchmarkPdfClassification.ProviderConfidential,
            ClassificationText = BenchmarkReportPackRenderer.ChatConsistencyStamp(document.Audience, options.Disclosure),
            Facts = facts,
            CreatedAtUtc = document.CreatedAtUtc,
            Keywords = KeywordsOf(document.SubjectLabel, "Chat consistency", audience),
            Paper = paper,
            AllowTableOfContents = document.Audience != BenchmarkReportAudience.ExecutiveSummary,
            FooterText = analysis + " · " + audience
        };
    }

    /// <summary>The analysis's name is the one it gets by default, <c>Chat consistency: &lt;model&gt;</c>, which the title already says.</summary>
    private static bool IsDefaultChatName(string name, BenchmarkReportFactSheet sheet)
        => string.Equals(name.Trim(), "Chat consistency: " + (sheet.SubjectLabel ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);

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

    /// <summary>
    /// A comparison-scope document's cover rows in place of Compared with or Peers: its models (their
    /// labels and count in a named copy, their count and letters in an anonymized one), a subset's
    /// coverage, counting the comparison's other models without naming them, the pricing basis, the
    /// suite or battery, the questions of its per-question matrix and the count of its runs.
    /// </summary>
    private static IEnumerable<BenchmarkPdfFact> ComparisonScopeFacts(
        BenchmarkReportDocument document, BenchmarkReportFactSheet sheet, BenchmarkPdfComparison? comparison,
        IReadOnlyCollection<long> runIds, BenchmarkReportPeerNaming naming)
    {
        yield return new("Models", ComparedWithText(sheet, naming));

        if (sheet.CoversAllEntries == false)
        {
            int covered = sheet.Peers.Count;
            int of = sheet.ComparisonEntryCount ?? comparison?.EntryCount ?? covered;
            int others = Math.Max(0, of - covered);
            yield return new("Coverage", Inv(covered) + " of " + ModelCount(of) + "; the other " + Inv(others)
                + (others == 1 ? " is" : " are") + " not part of this document");
        }

        yield return new("Pricing basis", BenchmarkReportPackRenderer.PricingBasisSummary(sheet));

        bool batteries = sheet.Questions.Any(q => !string.IsNullOrWhiteSpace(q.Reference));
        yield return new(batteries ? "Battery" : "Suite", document.SuiteName ?? sheet.SuiteName);
        yield return new("Questions", Inv(sheet.Questions.Count));
        yield return new(batteries ? "Member runs" : "Runs", Inv(runIds.Count));
    }

    /// <summary>
    /// The numbered comparison of a document: its id, and its display name and entry count when
    /// <see cref="BenchmarkReportDocument.Comparison"/> is loaded; null for a document without one.
    /// </summary>
    public static BenchmarkPdfComparison? ComparisonOf(BenchmarkReportDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Comparison is { } loaded)
        {
            string? name = string.IsNullOrWhiteSpace(loaded.DisplayName) ? null : loaded.DisplayName.Trim();
            return new BenchmarkPdfComparison(loaded.Id, name, loaded.EntryCount > 0 ? loaded.EntryCount : null);
        }
        return document.ComparisonId is int id ? new BenchmarkPdfComparison(id, null, null) : null;
    }

    /// <summary>
    /// <c>Comparison #12 — name</c>; <c>Comparison #12</c> when the name is unknown, and in an anonymized
    /// copy, because a comparison's name usually names its models.
    /// </summary>
    public static string ComparisonHeading(BenchmarkPdfComparison comparison, BenchmarkReportPeerNaming naming)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        string number = "Comparison #" + Inv(comparison.Id);
        return naming == BenchmarkReportPeerNaming.Named && !string.IsNullOrWhiteSpace(comparison.Name)
            ? number + " — " + comparison.Name
            : number;
    }

    /// <summary>
    /// The cover's Comparison row and the Markdown's Comparison line:
    /// <c>Comparison #12 — name · 3 models · computed 2026-09-20</c>, each unknown part left out.
    /// </summary>
    public static string ComparisonText(BenchmarkPdfComparison comparison, string? computedOn, BenchmarkReportPeerNaming naming)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        var parts = new List<string> { ComparisonHeading(comparison, naming) };
        if (comparison.EntryCount is int count) parts.Add(ModelCount(count));
        if (!string.IsNullOrWhiteSpace(computedOn)) parts.Add("computed " + computedOn.Trim());
        return string.Join(" · ", parts);
    }

    /// <summary>
    /// The day the comparison was computed on: the sheet's <c>comparison.pricedOn</c>, which a sheet
    /// priced from the catalog records; null when the sheet does not record it.
    /// </summary>
    public static string? ComparisonComputedOn(BenchmarkReportFactSheet? sheet)
    {
        var fact = sheet?.Facts.FirstOrDefault(f => string.Equals(f.Key, "comparison.pricedOn", StringComparison.Ordinal));
        return fact is { Available: true } && !string.IsNullOrWhiteSpace(fact.Display) ? fact.Display.Trim() : null;
    }

    /// <summary>
    /// A comparison-scope subject line: <see cref="ComparisonHeading"/> for a document covering every
    /// entry (the sheet's <c>coversAllEntries</c>); for a subset, followed by
    /// <c>· 2 of 5 models: A and B</c>, the covered models by label in a named copy and left out of an
    /// anonymized one. The total is the sheet's <c>comparisonEntryCount</c>, else the comparison's entry count.
    /// </summary>
    public static string ComparisonScopeSubjectLine(
        BenchmarkReportDocument document, BenchmarkPdfComparison comparison, BenchmarkReportPeerNaming naming)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(comparison);

        string heading = ComparisonHeading(comparison, naming);
        var facts = BenchmarkReportRenderService.ReadFacts(document.FactsJson);
        if (facts.CoversAllEntries) return heading;

        var covered = BenchmarkReportRenderService.CoveredModels(
            BenchmarkReportScope.Comparison, document.SubjectKey, document.SubjectLabel, document.CoveredEntryKeysJson, facts);
        int? total = facts.ComparisonEntryCount ?? comparison.EntryCount;
        string count = total is int of ? Inv(covered.Count) + " of " + ModelCount(of) : ModelCount(covered.Count);
        if (naming != BenchmarkReportPeerNaming.Named || covered.Count == 0) return heading + " · " + count;

        return heading + " · " + count + ": " + BenchmarkReportFormat.LetterList(covered.Select(m => m.Label).ToList());
    }

    /// <summary>
    /// <paramref name="text"/> cut to at most <paramref name="maxCharacters"/> characters, the last of them an
    /// ellipsis, at a word boundary when one falls in the last third; unchanged when it fits.
    /// </summary>
    public static string Ellipsize(string text, int maxCharacters)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (maxCharacters < 2) throw new ArgumentOutOfRangeException(nameof(maxCharacters));
        if (text.Length <= maxCharacters) return text;

        string head = text[..(maxCharacters - 1)];
        if (text[maxCharacters - 1] != ' ')
        {
            int space = head.LastIndexOf(' ');
            if (space >= maxCharacters * 2 / 3) head = head[..space];
        }
        return head.TrimEnd(' ', '·', '—', '-', ',') + "…";
    }

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
