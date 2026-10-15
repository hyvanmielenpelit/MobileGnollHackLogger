namespace Overseer.Services.Benchmarking.RootCause;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Overseer.Models;

/// <summary>
/// What each kind of Root Cause Analysis claim may cite (validator rules 32 to 36), read from a sheet's
/// <c>rca.*</c> facts. The prompt's CLAIM SUPPORT block lists it and
/// <see cref="BenchmarkReportPackValidator"/> checks against it, so the writer is told exactly the tokens
/// the checks accept.
///
/// <list type="bullet">
/// <item>A causal word (<see cref="CausalWords"/>) never appears in an observation slot
/// (<see cref="ObservationSlots"/>) or in an observation; in an inference slot (<see cref="InferenceSlots"/>)
/// it needs, in the same sentence, a token of a hypothesis the sheet holds (<c>rca.hypothesis.&lt;id&gt;.&lt;n&gt;.*</c>)
/// or of an axis (<c>rca.axis.&lt;axis&gt;.*</c>), whose status rests on the hypotheses.</item>
/// <item>A certainty word (<see cref="CertaintyWords"/>), anywhere, needs in the same sentence a token of a
/// hypothesis graded High, or the <c>rca.axis.&lt;axis&gt;.status</c> token of an axis whose root cause is
/// established. A certainty word after <c>not</c>, <c>never</c> or <c>cannot</c> is not one.</item>
/// <item>The conclusions cite every axis' <see cref="StatusKey"/>.</item>
/// </list>
/// </summary>
public sealed class RootCauseClaimSupport
{
    public const string HypothesisPrefix = "rca.hypothesis.";
    public const string AxisPrefix = "rca.axis.";

    /// <summary>Causal wording, matched as whole words and phrases, ignoring case.</summary>
    public static readonly IReadOnlyList<string> CausalWords = new[]
    {
        "because", "due to", "caused", "causes", "explains", "driven by", "root cause", "leads to", "results in"
    };

    /// <summary>Certainty wording, matched as whole words and phrases, ignoring case, unless negated.</summary>
    public static readonly IReadOnlyList<string> CertaintyWords = new[]
    {
        "established", "confirmed", "proven", "definitely", "certainly", "root cause was found"
    };

    /// <summary>The slots that state what was measured: a causal word there drops the paragraph (rule 32), as it drops an observation.</summary>
    public static readonly IReadOnlyList<string> ObservationSlots = new[]
    {
        BenchmarkReportSlots.Scope, BenchmarkReportSlots.EvidenceIntelligence, BenchmarkReportSlots.EvidenceSpeed, BenchmarkReportSlots.EvidenceCost
    };

    /// <summary>The slots that weigh the hypotheses: a causal word there needs a hypothesis or axis token in its sentence (rule 33).</summary>
    public static readonly IReadOnlyList<string> InferenceSlots = new[]
    {
        BenchmarkReportSlots.HypothesesIntelligence, BenchmarkReportSlots.HypothesesSpeed, BenchmarkReportSlots.HypothesesCost,
        BenchmarkReportSlots.Conclusions, BenchmarkReportSlots.NextRuns
    };

    /// <summary>The loci a writer hypothesis may name.</summary>
    public static readonly IReadOnlyList<string> Loci = new[]
    {
        RootCauseLoci.Model, RootCauseLoci.Chat, RootCauseLoci.Harness, RootCauseLoci.Suite, RootCauseLoci.Corpus, RootCauseLoci.Provider
    };

    internal static readonly Regex CausalRegex = PhraseRegex(CausalWords);

    internal static readonly Regex CertaintyRegex = PhraseRegex(
        CertaintyWords, @"(?<!(?<![\p{L}\p{N}])(?:not|never|cannot)\s+(?:yet\s+)?(?:be\s+|been\s+)?)");

    private static readonly Regex LocusSplitRegex = new(@"\s*(?:/|,|;|\bor\b|\band\b)\s*", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly Dictionary<string, BenchmarkReportFact> _facts;
    private readonly HashSet<string> _highPrefixes;

    private RootCauseClaimSupport(Dictionary<string, BenchmarkReportFact> facts)
    {
        _facts = facts;

        HypothesisPrefixes = facts.Keys
            .Select(HypothesisPrefixOf)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
        _highPrefixes = new HashSet<string>(
            HypothesisPrefixes.Where(p => string.Equals(Value(p + "grade"), RootCauseGrades.High, StringComparison.Ordinal)),
            StringComparer.Ordinal);
        Axes = RootCauseAxes.All.Where(a => facts.ContainsKey(StatusKey(a))).ToList();
        EstablishedAxes = Axes.Where(a => string.Equals(Value(StatusKey(a)), RootCauseStatuses.Established, StringComparison.Ordinal)).ToList();
    }

    /// <summary>The support of <paramref name="sheet"/>'s facts; the first fact of a repeated key counts.</summary>
    public static RootCauseClaimSupport From(BenchmarkReportFactSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        var facts = new Dictionary<string, BenchmarkReportFact>(StringComparer.Ordinal);
        foreach (var fact in sheet.Facts ?? new List<BenchmarkReportFact>())
        {
            facts.TryAdd(fact.Key, fact);
        }
        return new RootCauseClaimSupport(facts);
    }

    /// <summary>
    /// The sheet is a run- or battery-completion analysis: its root-cause origin says so, or, on a sheet
    /// without one, it is a per-model sheet without peers.
    /// </summary>
    public static bool IsCompletion(BenchmarkReportFactSheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        return sheet.RootCause is { } result
            ? result.Origin is RootCauseOrigins.RunCompletion or RootCauseOrigins.BatteryCompletion
            : !sheet.IsComparison && !sheet.IsChatConsistency && sheet.Peers.Count == 0;
    }

    /// <summary>Every hypothesis' token prefix, <c>rca.hypothesis.HQ1.1.</c>, ordinal.</summary>
    public IReadOnlyList<string> HypothesisPrefixes { get; }

    /// <summary>The axes the sheet states a status for, in document order.</summary>
    public IReadOnlyList<string> Axes { get; }

    /// <summary>The axes whose status is Root cause established, in document order.</summary>
    public IReadOnlyList<string> EstablishedAxes { get; }

    /// <summary><c>rca.axis.&lt;axis&gt;.status</c>.</summary>
    public static string StatusKey(string axis) => AxisPrefix + axis + ".status";

    /// <summary>The token is a fact of the sheet.</summary>
    public bool HasFact(string key) => _facts.ContainsKey(key);

    /// <summary>A token of a hypothesis the sheet holds.</summary>
    public bool IsHypothesisToken(string key)
        => HypothesisPrefixOf(key) is string prefix && HypothesisPrefixes.Contains(prefix, StringComparer.Ordinal) && _facts.ContainsKey(key);

    /// <summary>A token of an axis' status or best hypothesis that the sheet holds.</summary>
    public bool IsAxisToken(string key) => key.StartsWith(AxisPrefix, StringComparison.Ordinal) && _facts.ContainsKey(key);

    /// <summary>The token lets a causal word stand in its sentence (rule 33).</summary>
    public bool SupportsCausal(string key) => IsHypothesisToken(key) || IsAxisToken(key);

    /// <summary>The token lets a certainty word stand in its sentence (rule 34).</summary>
    public bool SupportsCertainty(string key)
        => (HypothesisPrefixOf(key) is string prefix && _highPrefixes.Contains(prefix) && _facts.ContainsKey(key))
           || EstablishedAxes.Any(a => string.Equals(StatusKey(a), key, StringComparison.Ordinal));

    /// <summary>What CLAIM SUPPORT lists for causal words: each hypothesis' tokens, then the axis tokens.</summary>
    public IReadOnlyList<string> CausalSupport()
    {
        var items = HypothesisPrefixes.Select(p => p + "*").ToList();
        items.AddRange(Axes.Select(a => AxisPrefix + a + ".*"));
        return items;
    }

    /// <summary>What CLAIM SUPPORT lists for certainty words: the High hypotheses' tokens, then the established axes' status tokens.</summary>
    public IReadOnlyList<string> CertaintySupport()
    {
        var items = HypothesisPrefixes.Where(_highPrefixes.Contains).Select(p => p + "*").ToList();
        items.AddRange(EstablishedAxes.Select(StatusKey));
        return items;
    }

    /// <summary>Every causal phrase in <paramref name="plain"/>, its whitespace folded.</summary>
    public static IEnumerable<string> CausalPhrases(string plain) => Phrases(CausalRegex, plain);

    /// <summary>Every certainty phrase in <paramref name="plain"/> that no negation precedes, its whitespace folded.</summary>
    public static IEnumerable<string> CertaintyPhrases(string plain) => Phrases(CertaintyRegex, plain);

    /// <summary>
    /// Whether <paramref name="locus"/> names only known loci: one or more of <see cref="Loci"/>, ignoring
    /// case, separated by <c>/</c>, commas, <c>or</c> or <c>and</c>.
    /// </summary>
    public static bool IsLocus(string? locus)
    {
        var parts = LocusSplitRegex.Split((locus ?? string.Empty).Trim())
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();
        return parts.Count > 0 && parts.All(p => Loci.Contains(p, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary><c>rca.hypothesis.HQ1.1.</c> for any key under it; null for any other key.</summary>
    public static string? HypothesisPrefixOf(string key)
    {
        if (!key.StartsWith(HypothesisPrefix, StringComparison.Ordinal)) return null;
        var parts = key.Split('.');
        return parts.Length >= 5 && int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out _)
            ? string.Join('.', parts.Take(4)) + "."
            : null;
    }

    /// <summary>A fact's raw string value, else its display; null when the fact is missing or unavailable.</summary>
    private string? Value(string key)
    {
        if (!_facts.TryGetValue(key, out var fact) || !fact.Available) return null;
        return fact.Value is JsonValue value && value.TryGetValue(out string? text) ? text : fact.Display;
    }

    private static IEnumerable<string> Phrases(Regex regex, string text)
        => regex.Matches(text ?? string.Empty).Select(m => Regex.Replace(m.Value, @"\s+", " "));

    /// <summary>
    /// Whole words and phrases of <paramref name="phrases"/>, ignoring case, longest first; a space inside a
    /// phrase matches any run of whitespace and hyphens, and a match never touches a letter, digit or hyphen.
    /// </summary>
    private static Regex PhraseRegex(IEnumerable<string> phrases, string? lookbehind = null)
    {
        var alternatives = phrases
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(p => p.Length)
            .ThenBy(p => p, StringComparer.Ordinal)
            .Select(p => string.Join(@"[\s-]+", Regex.Split(p.Trim(), @"[\s-]+").Select(Regex.Escape)));
        return new Regex(
            @"(?<![\p{L}\p{N}-])" + (lookbehind ?? string.Empty) + "(?:" + string.Join("|", alternatives) + @")(?![\p{L}\p{N}-])",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
