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
/// (<see cref="ObservationSlots"/>) or in an observation. In an inference slot (<see cref="InferenceSlots"/>)
/// a causal word that states a cause needs, in the same sentence, a token of a hypothesis graded Low or higher
/// (<c>rca.hypothesis.&lt;id&gt;.&lt;n&gt;.*</c>, its <c>.label</c> included), or of an axis (<c>rca.axis.&lt;axis&gt;.*</c>),
/// whose status rests on the hypotheses; in the next runs, an <c>rca.run.&lt;n&gt;.*</c> token as well. When that
/// support is only hypotheses graded Low, the sentence also holds a hedge word (<see cref="HedgeWords"/>). A
/// causal word directly after <c>not</c>, <c>never</c> or <c>cannot</c> rules a cause out: any hypothesis' token,
/// a Not supported one's included, or an axis token supports it.</item>
/// <item>A certainty word (<see cref="CertaintyWords"/>), anywhere, needs in the same sentence a token of a
/// hypothesis graded High that is no price effect (its <c>.priceCard</c> fact), its label included, or the
/// <c>rca.axis.&lt;axis&gt;.status</c> token of an axis whose root cause is established. A certainty word after
/// <c>not</c>, <c>never</c> or <c>cannot</c> is not one, nor is <c>confirmed</c> directly beside <c>critical error(s)</c>.</item>
/// <item>The conclusions cite every axis' <see cref="StatusKey"/>.</item>
/// </list>
/// </summary>
public sealed class RootCauseClaimSupport
{
    public const string HypothesisPrefix = "rca.hypothesis.";
    public const string AxisPrefix = "rca.axis.";
    public const string RunPrefix = "rca.run.";
    public const string QuestionPrefix = "rca.question.";

    /// <summary>The suffix of a hypothesis' label, a noun phrase that may sit inside a sentence.</summary>
    public const string LabelSuffix = ".label";

    /// <summary>The part of a price effect's <c>rca.hypothesis.&lt;id&gt;.&lt;n&gt;.priceCard</c> fact, true when it is known from the price cards.</summary>
    public const string PriceCardPart = "priceCard";

    /// <summary>Causal wording, matched as whole words and phrases, ignoring case.</summary>
    public static readonly IReadOnlyList<string> CausalWords = new[]
    {
        "because", "due to", "caused", "causes", "explains", "driven by", "root cause", "leads to", "results in"
    };

    /// <summary>Certainty wording, matched as whole words and phrases, ignoring case, unless negated.</summary>
    public static readonly IReadOnlyList<string> CertaintyWords = new[]
    {
        "established", "confirmed", "proven", "prove", "proves", "proved", "definitely", "certainly", "root cause was found"
    };

    /// <summary>Hedge wording, matched as whole words and phrases, ignoring case: a cause resting on Low hypotheses only needs one.</summary>
    public static readonly IReadOnlyList<string> HedgeWords = new[]
    {
        "may", "might", "could", "possibly", "probably", "likely", "perhaps", "suggests", "suggest", "consistent with"
    };

    /// <summary>The slots that state what was measured: a causal word there drops the paragraph (rule 32), as it drops an observation.</summary>
    public static readonly IReadOnlyList<string> ObservationSlots = new[]
    {
        BenchmarkReportSlots.Scope, BenchmarkReportSlots.EvidenceIntelligence, BenchmarkReportSlots.EvidenceSpeed, BenchmarkReportSlots.EvidenceCost,
        BenchmarkReportSlots.UnexpectedResults
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

    /// <summary>
    /// A negation directly before a causal phrase: <c>not</c>, <c>never</c> or <c>cannot</c>, with at most two of
    /// <c>be</c>, <c>been</c>, <c>the</c> and <c>a</c> between. <c>not only because</c> is no negation.
    /// </summary>
    private const string CausalNegation = @"(?<![\p{L}\p{N}])(?:not|never|cannot)\s+(?:(?:be|been|the|a)\s+){0,2}";

    /// <summary>Every causal phrase, negated or not.</summary>
    internal static readonly Regex CausalRegex = PhraseRegex(CausalWords);

    /// <summary>A causal phrase that rules a cause out: <c>is not due to</c>, <c>cannot be caused by</c>, <c>is not the root cause</c>.</summary>
    internal static readonly Regex NegatedCausalRegex = PhraseRegex(CausalWords, "(?<=" + CausalNegation + ")");

    /// <summary>A causal phrase that states a cause: every one that no negation precedes.</summary>
    internal static readonly Regex UnnegatedCausalRegex = PhraseRegex(CausalWords, "(?<!" + CausalNegation + ")");

    /// <summary><c>critical error</c> or <c>critical errors</c>, its words separated like a phrase's.</summary>
    private const string CriticalErrors = @"critical[\s-]+errors?";

    /// <summary>
    /// Every certainty phrase that no negation precedes. <c>confirmed</c> directly before or after <c>critical error(s)</c>
    /// is a status word, not a certainty: <c>confirmed critical errors</c>, <c>critical errors confirmed by both panel members</c>.
    /// </summary>
    internal static readonly Regex CertaintyRegex = PhraseRegex(
        CertaintyWords, @"(?<!(?<![\p{L}\p{N}])(?:not|never|cannot)\s+(?:yet\s+)?(?:be\s+|been\s+)?)",
        (phrase, pattern) => string.Equals(phrase, "confirmed", StringComparison.OrdinalIgnoreCase)
            ? @"(?<!(?<![\p{L}\p{N}-])" + CriticalErrors + @"[\s-]+)" + pattern + @"(?![\s-]+" + CriticalErrors + @"(?![\p{L}\p{N}-]))"
            : pattern);

    internal static readonly Regex HedgeRegex = PhraseRegex(HedgeWords);

    private static readonly Regex LocusSplitRegex = new(@"\s*(?:/|,|;|\bor\b|\band\b)\s*", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly Dictionary<string, BenchmarkReportFact> _facts;
    private readonly HashSet<string> _highPrefixes;
    private readonly HashSet<string> _supportedPrefixes;
    private readonly HashSet<string> _lowPrefixes;

    private RootCauseClaimSupport(Dictionary<string, BenchmarkReportFact> facts)
    {
        _facts = facts;

        HypothesisPrefixes = facts.Keys
            .Select(HypothesisPrefixOf)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
        // A price effect graded High is read off the price cards, not found by the runs: it supports no certainty word.
        _highPrefixes = new HashSet<string>(
            HypothesisPrefixes.Where(p => string.Equals(Value(p + "grade"), RootCauseGrades.High, StringComparison.Ordinal) && !IsTrue(p + PriceCardPart)),
            StringComparer.Ordinal);
        _supportedPrefixes = new HashSet<string>(
            HypothesisPrefixes.Where(p => RootCauseGrades.Rank(Value(p + "grade")) >= RootCauseGrades.Rank(RootCauseGrades.Low)),
            StringComparer.Ordinal);
        _lowPrefixes = new HashSet<string>(
            HypothesisPrefixes.Where(p => string.Equals(Value(p + "grade"), RootCauseGrades.Low, StringComparison.Ordinal)),
            StringComparer.Ordinal);
        SupportedHypothesisPrefixes = HypothesisPrefixes.Where(_supportedPrefixes.Contains).ToList();
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

    /// <summary>
    /// The token prefixes of the hypotheses graded Low or higher, ordinal: the ones whose tokens let a causal word
    /// state a cause. A hypothesis without a grade fact is not among them.
    /// </summary>
    public IReadOnlyList<string> SupportedHypothesisPrefixes { get; }

    /// <summary>The axes the sheet states a status for, in document order.</summary>
    public IReadOnlyList<string> Axes { get; }

    /// <summary>The axes whose status is Root cause established, in document order.</summary>
    public IReadOnlyList<string> EstablishedAxes { get; }

    /// <summary><c>rca.axis.&lt;axis&gt;.status</c>.</summary>
    public static string StatusKey(string axis) => AxisPrefix + axis + ".status";

    /// <summary>The token is a fact of the sheet.</summary>
    public bool HasFact(string key) => _facts.ContainsKey(key);

    /// <summary>A hypothesis' label, <c>rca.hypothesis.&lt;id&gt;.label</c>: a noun phrase, not a sentence.</summary>
    public static bool IsLabel(string key)
        => key.StartsWith(HypothesisPrefix, StringComparison.Ordinal) && key.EndsWith(LabelSuffix, StringComparison.Ordinal);

    /// <summary>The parts of a hypothesis whose display is a sentence or a list of clauses.</summary>
    private static readonly HashSet<string> SentenceShapedHypothesisParts = new(StringComparer.Ordinal)
    {
        "statement", "for", "against", "refutedIf", "gradeReasons"
    };

    private static readonly Regex ModelKeyPrefixRegex = new(@"^model\.[A-Z]+\.", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The kinds of fact <see cref="IsSentenceShaped"/> marks, in words, for the prompt and the validator's notes.</summary>
    public const string SentenceShapedKinds =
        "a hypothesis's statement, evidence for or against, refuted-if statement or grade reasons; a known fact; a check; "
        + "a research question or its signals; a model's list of flags; an unexpected result's summary; and a notable finding's summary";

    /// <summary>
    /// Whether a fact's display is a sentence, a clause or a list of clauses rather than a noun phrase, so the
    /// writer must give it a sentence of its own, or write it after a colon, never after a word of its own
    /// sentence: <c>rca.hypothesis.&lt;id&gt;.&lt;n&gt;.statement</c>, <c>.for</c>, <c>.against</c>,
    /// <c>.refutedIf</c> and <c>.gradeReasons</c>; <c>rca.known.&lt;n&gt;</c>; <c>rca.check.&lt;n&gt;</c>;
    /// <c>rca.question.&lt;n&gt;.text</c> and <c>.signals</c>;
    /// <c>rca.profile.&lt;T&gt;.flags</c>; <c>rca.anomaly.&lt;n&gt;.summary</c>; <c>notable.&lt;n&gt;.summary</c>; and an Executive Summary's
    /// <c>rcaDigest.keyFinding</c>, <c>.limits</c>, <c>.axis.&lt;axis&gt;</c>, <c>.hypothesis.&lt;n&gt;</c> and <c>.lead.&lt;n&gt;</c>. A hypothesis' label, a check's
    /// <c>rca.check.&lt;n&gt;.summary</c>, an unexpected result's <c>rca.anomaly.&lt;n&gt;.ref</c>, each single flag and
    /// <c>style.responseStyleConflict</c> are noun phrases and are never marked.
    /// </summary>
    public static bool IsSentenceShaped(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        string bare = ModelKeyPrefixRegex.Replace(key, string.Empty);
        string[] parts = bare.Split('.');

        if (parts.Length == 3 && parts[0] == "notable" && parts[2] == "summary") return true;
        if (parts[0] == "rcaDigest")
        {
            return parts.Length == 2
                ? (parts[1] is "keyFinding" or "limits")
                : (parts.Length == 3 && (parts[1] is "axis" or "hypothesis" or "lead"));
        }
        if (parts.Length < 3 || parts[0] != "rca") return false;

        return parts[1] switch
        {
            "hypothesis" => parts.Length == 5 && SentenceShapedHypothesisParts.Contains(parts[4]),
            "known" or "check" => parts.Length == 3,
            "question" => parts.Length == 4 && parts[3] is "text" or "signals",
            "profile" => parts.Length == 4 && parts[3] == "flags",
            "anomaly" => parts.Length == 4 && parts[3] == "summary",
            _ => false
        };
    }

    /// <summary>A token of a hypothesis the sheet holds, its label included.</summary>
    public bool IsHypothesisToken(string key) => _facts.ContainsKey(key) && PrefixesOf(key).Any();

    /// <summary>A token of an axis' status, best hypothesis or price note that the sheet holds.</summary>
    public bool IsAxisToken(string key) => key.StartsWith(AxisPrefix, StringComparison.Ordinal) && _facts.ContainsKey(key);

    /// <summary>A token of a next run (<c>rca.run.&lt;n&gt;.*</c>) that the sheet holds.</summary>
    public bool IsRunToken(string key) => key.StartsWith(RunPrefix, StringComparison.Ordinal) && _facts.ContainsKey(key);

    /// <summary>A token of a hypothesis graded Low or higher that the sheet holds, its label included.</summary>
    public bool IsSupportedHypothesisToken(string key) => _facts.ContainsKey(key) && PrefixesOf(key).Any(_supportedPrefixes.Contains);

    /// <summary>A token of a hypothesis graded Low or higher whose hypotheses at that grade or higher are all graded exactly Low.</summary>
    public bool IsLowHypothesisToken(string key)
        => IsSupportedHypothesisToken(key) && PrefixesOf(key).Where(_supportedPrefixes.Contains).All(_lowPrefixes.Contains);

    /// <summary>
    /// The token lets a causal word state a cause in its sentence of <paramref name="slot"/> (rule 33): a token of a
    /// hypothesis graded Low or higher, or of an axis; in the next runs, a run token as well, since each run names
    /// what it settles.
    /// </summary>
    public bool SupportsCausal(string key, string? slot = null)
        => IsSupportedHypothesisToken(key) || IsAxisToken(key) || IsNextRunToken(key, slot);

    /// <summary>
    /// The token lets a negated causal word rule a cause out in its sentence of <paramref name="slot"/> (rule 33):
    /// any hypothesis' token, a Not supported one's included, or an axis token; in the next runs, a run token as well.
    /// </summary>
    public bool SupportsNegatedCausal(string key, string? slot = null)
        => IsHypothesisToken(key) || IsAxisToken(key) || IsNextRunToken(key, slot);

    /// <summary>
    /// A sentence of <paramref name="slot"/> citing <paramref name="tokens"/> states a cause on hypotheses graded Low
    /// only, so it needs a hedge word (rule 33): it has causal support, and every supporting token is a hypothesis
    /// token graded Low, with no axis token and no run token among them.
    /// </summary>
    public bool NeedsHedge(IEnumerable<string> tokens, string? slot = null)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        var supporting = tokens.Where(t => SupportsCausal(t, slot)).ToList();
        return supporting.Count > 0 && supporting.All(IsLowHypothesisToken);
    }

    /// <summary>A run token in the next runs.</summary>
    private bool IsNextRunToken(string key, string? slot)
        => string.Equals(slot, BenchmarkReportSlots.NextRuns, StringComparison.Ordinal) && IsRunToken(key);

    /// <summary>The token lets a certainty word stand in its sentence (rule 34).</summary>
    public bool SupportsCertainty(string key)
        => (_facts.ContainsKey(key) && PrefixesOf(key).Any(_highPrefixes.Contains))
           || EstablishedAxes.Any(a => string.Equals(StatusKey(a), key, StringComparison.Ordinal));

    /// <summary>
    /// The hypothesis prefixes a key belongs to: its own for a key under one, and for a label keyed by the
    /// rule id alone (<c>rca.hypothesis.HQ1.label</c>), every hypothesis of that rule.
    /// </summary>
    private IEnumerable<string> PrefixesOf(string key)
    {
        if (HypothesisPrefixOf(key) is string prefix)
        {
            if (HypothesisPrefixes.Contains(prefix, StringComparer.Ordinal)) yield return prefix;
            yield break;
        }
        if (!IsLabel(key) || key.Length <= HypothesisPrefix.Length + LabelSuffix.Length) yield break;

        string id = key[HypothesisPrefix.Length..^LabelSuffix.Length];
        foreach (string rule in HypothesisPrefixes.Where(h => h.StartsWith(HypothesisPrefix + id + ".", StringComparison.Ordinal)))
        {
            yield return rule;
        }
    }

    /// <summary>What CLAIM SUPPORT lists for causal words: the tokens of each hypothesis graded Low or higher, then the axis tokens.</summary>
    public IReadOnlyList<string> CausalSupport()
    {
        var items = SupportedHypothesisPrefixes.Select(p => p + "*").ToList();
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

    /// <summary>Every causal phrase in <paramref name="plain"/> that a negation precedes, its whitespace folded.</summary>
    public static IEnumerable<string> NegatedCausalPhrases(string plain) => Phrases(NegatedCausalRegex, plain);

    /// <summary>Every causal phrase in <paramref name="plain"/> that no negation precedes, its whitespace folded.</summary>
    public static IEnumerable<string> UnnegatedCausalPhrases(string plain) => Phrases(UnnegatedCausalRegex, plain);

    /// <summary>Every hedge phrase in <paramref name="plain"/>, its whitespace folded.</summary>
    public static IEnumerable<string> HedgePhrases(string plain) => Phrases(HedgeRegex, plain);

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

    /// <summary>The fact is present, available and its value is <c>true</c>.</summary>
    private bool IsTrue(string key)
        => _facts.TryGetValue(key, out var fact) && fact.Available
           && fact.Value is JsonValue value && value.TryGetValue(out bool flag) && flag;

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
    /// <paramref name="alternative"/>, when given, rewrites a phrase's pattern from the phrase and that pattern.
    /// </summary>
    private static Regex PhraseRegex(IEnumerable<string> phrases, string? lookbehind = null, Func<string, string, string>? alternative = null)
    {
        var alternatives = phrases
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(p => p.Length)
            .ThenBy(p => p, StringComparer.Ordinal)
            .Select(p => (Phrase: p, Pattern: string.Join(@"[\s-]+", Regex.Split(p.Trim(), @"[\s-]+").Select(Regex.Escape))))
            .Select(x => alternative == null ? x.Pattern : alternative(x.Phrase, x.Pattern));
        return new Regex(
            @"(?<![\p{L}\p{N}-])" + (lookbehind ?? string.Empty) + "(?:" + string.Join("|", alternatives) + @")(?![\p{L}\p{N}-])",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
