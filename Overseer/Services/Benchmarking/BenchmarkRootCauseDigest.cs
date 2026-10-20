namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking.RootCause;

/// <summary>
/// The digest of a stored Root Cause Analysis that an Executive Summary is written from, built in code from the
/// document's fact sheet (<see cref="BenchmarkReportFactSheet.RootCause"/> and its <c>rca.*</c> facts) and its writer
/// output, never from its rendered prose. Each item is one <c>rcaDigest.*</c> fact the Executive Summary may cite:
///
/// <list type="bullet">
/// <item><c>rcaDigest.keyFinding</c>: the Root Cause Analysis' headline, its tokens resolved, as plain text.</item>
/// <item><c>rcaDigest.axis.&lt;axis&gt;</c>: each axis's status, its best hypothesis and that hypothesis' grade.</item>
/// <item><c>rcaDigest.hypothesis.&lt;n&gt;</c>: the top hypotheses of each axis, at most <see cref="MaxHypothesesPerAxis"/> in
/// a run or battery document and <see cref="MaxComparisonHypothesesPerAxis"/> in a comparison, best grade first, each with
/// its grade and locus; a ruled-out one reads as ruled out.</item>
/// <item><c>rcaDigest.lead.&lt;n&gt;</c>: at most <see cref="MaxLeads"/> of the writer's own hypotheses, each an untested lead.</item>
/// <item><c>rcaDigest.nextRun.&lt;n&gt;</c> and <c>rcaDigest.check.&lt;n&gt;</c>: at most <see cref="MaxNextRuns"/> next runs, with
/// their purpose, and at most <see cref="MaxChecks"/> checks, in code's order.</item>
/// <item><c>rcaDigest.limits</c>: the control limit and the replicate limit in one sentence.</item>
/// </list>
///
/// <para>Each fact's raw value says what the item is: an axis' status, a hypothesis' grade, <see cref="LeadValue"/> for a
/// lead, a run's or a check's number, so the validator can ask for a hedge beside a lead or a Low grade. The digest's text
/// is its facts as <c>key = display</c> lines, and its SHA-256 is stored with the summary
/// (<see cref="BenchmarkReportRootCauseDigestSource"/>).</para>
/// </summary>
public sealed class BenchmarkRootCauseDigest
{
    /// <summary>The prefix of every digest fact.</summary>
    public const string Prefix = "rcaDigest.";

    public const string KeyFindingKey = Prefix + "keyFinding";
    public const string LimitsKey = Prefix + "limits";
    public const string AxisPrefix = Prefix + "axis.";
    public const string HypothesisPrefix = Prefix + "hypothesis.";
    public const string LeadPrefix = Prefix + "lead.";
    public const string NextRunPrefix = Prefix + "nextRun.";
    public const string CheckPrefix = Prefix + "check.";

    /// <summary>The raw value of a lead's fact: the writer's own hypothesis, untested, at Low.</summary>
    public const string LeadValue = "Lead";

    public const int MaxHypothesesPerAxis = 3;
    public const int MaxComparisonHypothesesPerAxis = 2;
    public const int MaxLeads = 2;
    public const int MaxNextRuns = 2;
    public const int MaxChecks = 2;

    private static readonly Regex TokenRegex = new(@"\{\{([^{}]+)\}\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private BenchmarkRootCauseDigest(long documentId, List<BenchmarkReportFact> facts)
    {
        DocumentId = documentId;
        Facts = facts;
        Text = string.Concat(facts.Select(f => f.Key + " = " + f.Display + "\n"));
        Sha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Text)));
    }

    /// <summary>The Root Cause Analysis document the digest was built from.</summary>
    public long DocumentId { get; }

    /// <summary>The digest's facts, in digest order: key finding, axes, hypotheses, leads, next runs, checks, limits.</summary>
    public IReadOnlyList<BenchmarkReportFact> Facts { get; }

    /// <summary>The facts as <c>key = display</c> lines joined with <c>\n</c>.</summary>
    public string Text { get; }

    /// <summary>The lower-case hex SHA-256 of <see cref="Text"/>.</summary>
    public string Sha256 { get; }

    /// <summary>A digest key of the Executive Summary: every <c>rcaDigest.*</c> fact.</summary>
    public static bool IsDigestKey(string? key) => key != null && key.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>A next run's or a check's digest key, which an item of What to look at next cites.</summary>
    public static bool IsNextStepKey(string? key)
        => key != null && (key.StartsWith(NextRunPrefix, StringComparison.Ordinal) || key.StartsWith(CheckPrefix, StringComparison.Ordinal));

    /// <summary>An axis', a hypothesis' or a lead's digest key, which a causal word of the Executive Summary needs.</summary>
    public static bool IsCauseKey(string? key)
        => key != null && (key.StartsWith(AxisPrefix, StringComparison.Ordinal) || key.StartsWith(HypothesisPrefix, StringComparison.Ordinal)
                           || key.StartsWith(LeadPrefix, StringComparison.Ordinal));

    /// <summary>
    /// The digest of a stored Root Cause Analysis; null when the document is no Root Cause Analysis, or its fact sheet
    /// holds no root-cause result.
    /// </summary>
    public static BenchmarkRootCauseDigest? From(BenchmarkReportDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Audience != BenchmarkReportAudience.RootCauseAnalysis || string.IsNullOrWhiteSpace(document.FactsJson)) return null;

        var sheet = BenchmarkReportJson.Deserialize<BenchmarkReportFactSheet>(document.FactsJson);
        var output = string.IsNullOrWhiteSpace(document.WriterOutputJson)
            ? new BenchmarkReportWriterOutput()
            : BenchmarkReportJson.Deserialize<BenchmarkReportWriterOutput>(document.WriterOutputJson);
        return From(document.Id, sheet, output);
    }

    /// <summary>The digest of a Root Cause Analysis' sheet and writer output; null when the sheet holds no root-cause result.</summary>
    public static BenchmarkRootCauseDigest? From(long documentId, BenchmarkReportFactSheet sheet, BenchmarkReportWriterOutput output)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(output);
        var result = sheet.RootCause;
        if (result == null) return null;

        var facts = new Dictionary<string, BenchmarkReportFact>(StringComparer.Ordinal);
        foreach (var fact in sheet.Facts ?? new List<BenchmarkReportFact>()) facts.TryAdd(fact.Key, fact);
        string Resolve(string? text) => ResolveTokens(text, facts);

        var digest = new List<BenchmarkReportFact>();
        void Add(string key, JsonNode? value, string display)
        {
            if (!string.IsNullOrWhiteSpace(display)) digest.Add(new BenchmarkReportFact { Key = key, Value = value, Display = display.Trim() });
        }

        Add(KeyFindingKey, null, WithPeriod(Resolve(output.Headline)));

        var byKey = result.Hypotheses.GroupBy(h => h.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        foreach (var axis in RootCauseAxes.All)
        {
            var status = result.Axes.FirstOrDefault(a => a.Axis == axis);
            if (status == null) continue;
            var best = status.BestHypothesis != null && byKey.TryGetValue(status.BestHypothesis, out var found) ? found : null;
            Add(AxisPrefix + axis, JsonValue.Create(status.Status), AxisSentence(status, best));
        }

        int cap = sheet.IsComparison ? MaxComparisonHypothesesPerAxis : MaxHypothesesPerAxis;
        int n = 0;
        foreach (var axis in RootCauseAxes.All)
        {
            var top = result.Hypotheses
                .Select((h, i) => (Hypothesis: h, Index: i))
                .Where(x => x.Hypothesis.Axis == axis && x.Hypothesis.Kind != "null" && !x.Hypothesis.PriceEffect
                            && !string.IsNullOrWhiteSpace(x.Hypothesis.Statement))
                .OrderByDescending(x => RootCauseGrades.Rank(x.Hypothesis.Grade))
                .ThenBy(x => x.Index)
                .Take(cap)
                .Select(x => x.Hypothesis);
            foreach (var h in top)
            {
                Add(HypothesisPrefix + Inv(++n), JsonValue.Create(h.Grade), HypothesisSentence(h));
            }
        }

        int lead = 0;
        foreach (var writer in (output.WriterHypotheses ?? new List<BenchmarkReportWriterHypothesis>())
                     .Where(w => !string.IsNullOrWhiteSpace(w.Statement))
                     .Take(MaxLeads))
        {
            Add(LeadPrefix + Inv(++lead), JsonValue.Create(LeadValue),
                "An untested lead (Low), from the graders' comments: " + LowerFirst(WithPeriod(Resolve(writer.Statement))));
        }

        foreach (var run in result.RecommendedRuns.OrderBy(r => r.Number).Take(MaxNextRuns))
        {
            string prefix = RootCauseClaimSupport.RunPrefix + Inv(run.Number) + ".";
            string summary = facts.TryGetValue(prefix + "summary", out var s) && s.Available ? s.Display : RootCauseNextRuns.Summary(run);
            string purpose = facts.TryGetValue(prefix + "purpose", out var p) && p.Available ? p.Display : RootCausePurposes.Text(run.Purpose);
            Add(NextRunPrefix + Inv(run.Number), JsonValue.Create(run.Number), Trim(summary) + " (purpose " + purpose + ")");
        }

        foreach (var check in result.Checks.OrderBy(c => c.Number).Take(MaxChecks))
        {
            string key = "rca.check." + Inv(check.Number) + ".summary";
            string summary = facts.TryGetValue(key, out var s) && s.Available ? s.Display : RootCauseNextRuns.Summary(check);
            Add(CheckPrefix + Inv(check.Number), JsonValue.Create(check.Number), Trim(summary));
        }

        Add(LimitsKey, null, LimitsSentence(result, facts));

        return new BenchmarkRootCauseDigest(documentId, digest);
    }

    /// <summary>
    /// <c>Speed: probable cause — tool rounds multiply time (graded Moderate).</c>; <c>Intelligence: not established; every
    /// hypothesis was ruled out.</c>; for an axis without a best hypothesis, its reason after the status.
    /// </summary>
    internal static string AxisSentence(RootCauseAxisStatus status, RootCauseHypothesis? best)
    {
        string head = RootCauseAxes.Name(status.Axis) + ": " + RootCauseStatuses.Display(status.Status).ToLowerInvariant();
        if (best == null)
        {
            string reason = status.Reason == RootCauseAxisStatus.AllRuledOutReason
                ? "every hypothesis was ruled out"
                : LowerFirst(Trim(status.Reason));
            return head + (reason.Length == 0 ? "." : "; " + reason + ".");
        }

        string what = !string.IsNullOrWhiteSpace(best.ShortStatement) ? best.ShortStatement
            : !string.IsNullOrWhiteSpace(best.Label) ? best.Label
            : best.Statement;
        string grade = best.PriceEffect ? RootCauseGrades.PriceCardWord.ToLowerInvariant() : "graded " + RootCauseGrades.Display(best.Grade);
        return head + " — " + LowerFirst(Trim(what)) + " (" + grade + ").";
    }

    /// <summary>
    /// <c>Speed: the model makes more tool rounds — graded Moderate, locus chat.</c>; a ruled-out one
    /// <c>Speed, ruled out: … — graded Not supported, locus model.</c>
    /// </summary>
    internal static string HypothesisSentence(RootCauseHypothesis h)
    {
        bool ruledOut = h.Grade == RootCauseGrades.NotSupported;
        string head = RootCauseAxes.Name(h.Axis) + (ruledOut ? ", ruled out: " : ": ");
        string locus = string.IsNullOrWhiteSpace(h.Locus) ? string.Empty : ", locus " + h.Locus.Trim();
        return head + LowerFirst(Trim(h.Statement)) + " — graded " + RootCauseGrades.Display(h.Grade) + locus + ".";
    }

    /// <summary>The control limit and the replicate limit in one sentence.</summary>
    private static string LimitsSentence(RootCauseResult result, IReadOnlyDictionary<string, BenchmarkReportFact> facts)
    {
        string control = facts.TryGetValue("rca.controls.limit", out var limit) && limit.Available
            ? limit.Display
            : result.Origin is RootCauseOrigins.RunCompletion or RootCauseOrigins.BatteryCompletion
                ? result.ControlLimit == null ? "no cap" : "capped at " + RootCauseGrades.Display(result.ControlLimit)
                : "none, since the models are compared with each other";
        string replicates = string.IsNullOrWhiteSpace(result.GradeCap)
            ? "none, since every model has two or more runs per suite"
            : LowerFirst(Trim(result.GradeCap));
        return "Control limit: " + Trim(control) + "; replicate limit: " + replicates + ".";
    }

    /// <summary>
    /// <paramref name="text"/> with every token resolved: a fact token to its display (<c>not available</c> without one),
    /// <c>{{subject}}</c> to <c>the model under test</c>, <c>{{peer:X}}</c> and <c>{{model:X}}</c> to <c>Model X</c>; the
    /// first letter capitalized.
    /// </summary>
    internal static string ResolveTokens(string? text, IReadOnlyDictionary<string, BenchmarkReportFact> facts)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        string resolved = TokenRegex.Replace(text, m =>
        {
            string key = m.Groups[1].Value.Trim();
            if (key == "subject") return "the model under test";
            if (key.StartsWith("peer:", StringComparison.Ordinal)) return "Model " + key[5..].Trim();
            if (key.StartsWith("model:", StringComparison.Ordinal)) return "Model " + key[6..].Trim();
            return facts.TryGetValue(key, out var fact) && fact.Available ? fact.Display : BenchmarkReportFacts.NotAvailable;
        });
        resolved = Regex.Replace(resolved.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' '), @"\s{2,}", " ").Trim();
        return resolved.Length > 0 && char.IsLower(resolved[0]) ? char.ToUpperInvariant(resolved[0]) + resolved[1..] : resolved;
    }

    /// <summary>
    /// The SHA-256 of a sheet's facts, its digest facts left out: two sheets with the same hash state the same data, so an
    /// Executive Summary may be written from a stored Root Cause Analysis whose sheet has the new sheet's hash.
    /// </summary>
    public static string FactsSha256(BenchmarkReportFactSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        var facts = (sheet.Facts ?? new List<BenchmarkReportFact>())
            .Where(f => !IsDigestKey(f.Key))
            .OrderBy(f => f.Key, StringComparer.Ordinal)
            .ToList();
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(BenchmarkReportJson.Serialize(facts))));
    }

    /// <summary>
    /// <paramref name="sheet"/>, an Executive Summary's copy, written from this digest: its digest facts replaced by this
    /// digest's, in key order with the rest, and its <see cref="BenchmarkReportFactSheet.RootCauseDigest"/> naming the
    /// document, the digest's hash and <paramref name="source"/>.
    /// </summary>
    public void ApplyTo(BenchmarkReportFactSheet sheet, string source)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        sheet.Facts = (sheet.Facts ?? new List<BenchmarkReportFact>())
            .Where(f => !IsDigestKey(f.Key))
            .Concat(Facts.Select(f => new BenchmarkReportFact { Key = f.Key, Value = f.Value?.DeepClone(), Display = f.Display, Available = true }))
            .OrderBy(f => f.Key, StringComparer.Ordinal)
            .ToList();
        sheet.RootCauseDigest = new BenchmarkReportRootCauseDigestSource
        {
            DocumentId = DocumentId,
            Sha256 = Sha256,
            Source = source
        };
    }

    /// <summary><paramref name="sheet"/>, an Executive Summary's copy, written without a digest for <paramref name="note"/>.</summary>
    public static void ApplyNone(BenchmarkReportFactSheet sheet, string note)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        sheet.Facts = (sheet.Facts ?? new List<BenchmarkReportFact>()).Where(f => !IsDigestKey(f.Key)).ToList();
        sheet.RootCauseDigest = new BenchmarkReportRootCauseDigestSource { Source = BenchmarkRootCauseSources.None, Note = note };
    }

    /// <summary>The digest facts of a sheet, in digest order: key finding, axes, hypotheses, leads, next runs, checks, limits.</summary>
    public static IReadOnlyList<BenchmarkReportFact> FactsOf(BenchmarkReportFactSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        return (sheet.Facts ?? new List<BenchmarkReportFact>())
            .Where(f => IsDigestKey(f.Key))
            .OrderBy(f => GroupRank(f.Key))
            .ThenBy(f => AxisRank(f.Key))
            .ThenBy(f => NumberOf(f.Key))
            .ThenBy(f => f.Key, StringComparer.Ordinal)
            .ToList();
    }

    private static int GroupRank(string key)
        => key == KeyFindingKey ? 0
            : key.StartsWith(AxisPrefix, StringComparison.Ordinal) ? 1
            : key.StartsWith(HypothesisPrefix, StringComparison.Ordinal) ? 2
            : key.StartsWith(LeadPrefix, StringComparison.Ordinal) ? 3
            : key.StartsWith(NextRunPrefix, StringComparison.Ordinal) ? 4
            : key.StartsWith(CheckPrefix, StringComparison.Ordinal) ? 5
            : key == LimitsKey ? 6
            : 7;

    private static int AxisRank(string key)
    {
        if (!key.StartsWith(AxisPrefix, StringComparison.Ordinal)) return 0;
        int index = RootCauseAxes.All.ToList().IndexOf(key[AxisPrefix.Length..]);
        return index < 0 ? int.MaxValue : index;
    }

    private static int NumberOf(string key)
    {
        int dot = key.LastIndexOf('.');
        return dot > 0 && int.TryParse(key.AsSpan(dot + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? n : 0;
    }

    private static string Inv(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Trim(string? text) => (text ?? string.Empty).Trim().TrimEnd('.').Trim();

    private static string WithPeriod(string text)
    {
        string trimmed = (text ?? string.Empty).Trim();
        return trimmed.Length == 0 || trimmed.EndsWith('.') || trimmed.EndsWith('?') || trimmed.EndsWith('!') ? trimmed : trimmed + ".";
    }

    /// <summary>The text with its first letter lowered, unless it opens a name (two capitals, or <c>Model</c>, <c>Q</c> and a digit).</summary>
    private static string LowerFirst(string text)
    {
        if (string.IsNullOrEmpty(text) || !char.IsUpper(text[0])) return text;
        if (text.Length > 1 && (char.IsUpper(text[1]) || char.IsDigit(text[1]))) return text;
        if (text.StartsWith("Model ", StringComparison.Ordinal)) return text;
        return char.ToLowerInvariant(text[0]) + text[1..];
    }
}
