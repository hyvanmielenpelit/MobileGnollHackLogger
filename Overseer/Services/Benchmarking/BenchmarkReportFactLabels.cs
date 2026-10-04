namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

/// <summary>
/// The human label of every fact key <see cref="BenchmarkReportFacts"/> defines, so a rendered
/// document never prints a raw key and one figure never has two names. Explicit entries cover the
/// fixed keys; rules cover the patterned dimension, band, authored-band and peer keys. A key neither covers gets a
/// readable fallback: its dotted parts split at camel case and joined as words.
/// </summary>
public static class BenchmarkReportFactLabels
{
    private static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // Identity and configuration
        ["subject.label"] = "Model under test",
        ["subject.provider"] = "Provider of the model under test",
        ["subject.modelId"] = "Model ID of the model under test",
        ["subject.thinkingLevel"] = "Thinking level",
        ["subject.runs"] = "Runs",
        ["subject.state"] = "Comparability state",
        ["suite.name"] = "Suite",
        ["suite.questions"] = "Questions",
        ["config.chat"] = "Chat configuration under test",
        ["comparison.models"] = "Models compared",
        ["comparison.peerRuns"] = "Runs per peer",
        ["comparison.pricingBasis"] = "Pricing basis",
        ["comparison.pricingBasisKind"] = "Pricing basis kind",
        ["comparison.pricedOn"] = "Catalog price date",
        ["comparison.signature"] = "Comparability signature",

        // Quality
        ["quality.index"] = "Intelligence Index",
        ["quality.interval"] = "95 % interval",
        ["quality.intervalSpan"] = "Interval span",
        ["quality.intervalBasis"] = "Interval basis",
        ["quality.rank"] = "Intelligence rank",
        ["quality.intervalOverlap"] = "Interval overlap",
        ["quality.peerMedian"] = "Peer median Intelligence Index",
        ["quality.peerBest"] = "Best peer Intelligence Index",
        ["quality.rawIndex"] = "Intelligence Index before the critical-error cap",
        ["quality.unweightedMean"] = "Unweighted mean answer quality",
        ["quality.scoredItems"] = "Scored questions",

        // Speed
        ["speed.modelTimeP50"] = "Median answer time",
        ["speed.modelTimeMean"] = "Mean answer time",
        ["speed.modelTimeP90"] = "90th-percentile answer time",
        ["speed.ttftP50"] = "Median time to first token",
        ["speed.rank"] = "Speed rank",

        // Cost and tokens
        ["cost.perQuestion"] = "Cost per question",
        ["cost.perRun"] = "Cost per run",
        ["cost.rank"] = "Cost rank",
        ["cost.basis"] = "Cost basis",
        ["cost.pricingAsOf"] = "Price card date",
        ["cost.totalRunPerRun"] = "Total cost per run (every grading and synthesis role; report writer excluded)",
        ["tokens.inputPerQuestion"] = "Input tokens per question",
        ["tokens.outputPerQuestion"] = "Output tokens per question",

        // Errors and claims
        ["answers.scored"] = "Scored answers",
        ["errors.critical"] = "Critical errors",
        ["claims.supported"] = "Claims the verifier supported",
        ["claims.refuted"] = "Claims the verifier refuted (the answers' own claims)",
        ["claims.indeterminate"] = "Claims the verifier could not decide",
        ["claims.refutedAnswerSentences"] = "Refuted answer sentences, accused sentences included",

        // Tools
        ["tools.callsPerQuestion"] = "Tool calls per question",
        ["tools.callsPerQuestion.peerMean"] = "Peer mean tool calls per question",
        ["tools.share.sourceCode"] = "Source code share",
        ["tools.share.wiki"] = "Wiki share",
        ["tools.share.structuredLookup"] = "Structured lookup share",
        ["tools.share.knowledgeBase"] = "Knowledge base share",
        ["tools.share.other"] = "Other tools share",
        ["tools.zeroKnowledgeBaseAnswers"] = "Answers without a knowledge-base article",
        ["tools.failed"] = "Failed tool calls",
        ["tools.refusedByBudget"] = "Calls refused by the tool budget",

        // Panel
        ["panel.meanAbsDelta"] = "Panel mean absolute difference",
        ["panel.icc"] = "Intraclass correlation, ICC(A,1)",
        ["panel.disagreements"] = "Panel disagreements",
        ["panel.memberAAlone"] = "Panel member A alone",
        ["panel.memberBAlone"] = "Panel member B alone",
        ["panel.referenceReaderIndex"] = "Reference reader (advisory, third provider)",
        ["panel.referenceReaderOffset"] = "Reference reader's mean offset from the panel",
        ["panel.judgeDependentPairs"] = "Judge-dependent pairs",

        // Style
        ["style.responseStyleConflict"] = "Response-style conflict",

        // Scoring and provenance
        ["scoring.weights"] = "Dimension weights",
        ["scoring.levels"] = "Level scores",
        ["scoring.criticalErrorCap"] = "Critical-error cap",
        ["scoring.methodVersion"] = "Scoring method version",
        ["run.harnessVersion"] = "Harness version",
        ["run.ids"] = "Run IDs",
        ["run.dates"] = "Run dates",
        ["run.promptSha256"] = "System prompt SHA-256 prefix",
        ["run.toolGuidesSha256"] = "Tool guides SHA-256 prefix",
    };

    /// <summary>
    /// The labels of a peer's own facts (<c>peer.&lt;letter&gt;.&lt;suffix&gt;</c>) by suffix, <c>{0}</c> standing
    /// for <c>Model &lt;letter&gt;</c>. A label never names the peer; the renderer resolves the naming.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> PeerLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["quality.index"] = "{0}'s Intelligence Index",
        ["quality.interval"] = "{0}'s 95 % interval",
        ["quality.rank"] = "{0}'s intelligence rank",
        ["speed.medianSeconds"] = "{0}'s median answer time",
        ["cost.perQuestion"] = "{0}'s cost per question",
        ["runs"] = "{0}'s runs",
        ["intervalOverlap"] = "Interval overlap with {0}",
        ["pairedDifference"] = "Paired difference from {0}",
        ["pairedInterval"] = "Paired-bootstrap interval against {0}",
        ["pairedExcludesZero"] = "Paired interval against {0} relative to zero",
        ["sharedQuestions"] = "Questions shared with {0}",
    };

    private static readonly IReadOnlyDictionary<string, string> DimensionNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["accuracy"] = "Accuracy",
        ["completeness"] = "Completeness",
        ["conciseness"] = "Conciseness",
        ["readability"] = "Readability",
    };

    private static readonly IReadOnlyDictionary<string, string> BandNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["simple"] = "Simple",
        ["intermediate"] = "Intermediate",
        ["advanced"] = "Advanced",
    };

    /// <summary>The dimension keys in report order, with their names.</summary>
    public static IReadOnlyList<(string Key, string Name)> Dimensions { get; } =
        new[] { "accuracy", "completeness", "conciseness", "readability" }.Select(d => (d, DimensionNames[d])).ToList();

    /// <summary>The difficulty band keys in report order, with their names.</summary>
    public static IReadOnlyList<(string Key, string Name)> Bands { get; } =
        new[] { "simple", "intermediate", "advanced" }.Select(b => (b, BandNames[b])).ToList();

    /// <summary>The label of a fact key; a readable fallback for a key no entry or rule covers.</summary>
    public static string Label(string key)
        => TryLabel(key, out string label) ? label : Fallback(key);

    /// <summary>The label from an explicit entry or a patterned-key rule; false for a key neither covers.</summary>
    public static bool TryLabel(string? key, out string label)
    {
        label = string.Empty;
        if (string.IsNullOrWhiteSpace(key)) return false;
        key = key.Trim();

        if (Labels.TryGetValue(key, out var explicitLabel))
        {
            label = explicitLabel;
            return true;
        }

        string[] parts = key.Split('.');

        // dimension.<d>, dimension.<d>.peerMean, dimension.<d>.difference
        if (parts[0] == "dimension" && parts.Length is 2 or 3 && DimensionNames.TryGetValue(parts[1], out var dimension))
        {
            string? suffix = parts.Length == 2 ? " score" : Comparison(parts[2]);
            if (suffix == null) return false;
            label = dimension + suffix;
            return true;
        }

        // band.<b>.questions, band.<b>.score, band.<b>.peerMean, band.<b>.difference
        if (parts[0] == "band" && parts.Length == 3 && BandNames.TryGetValue(parts[1], out var band))
        {
            string? suffix = parts[2] switch
            {
                "questions" => " band questions",
                "score" => " band score",
                _ => Comparison(parts[2]) is string s ? " band" + s : null
            };
            if (suffix == null) return false;
            label = band + suffix;
            return true;
        }

        // bands.authored.<b>
        if (parts[0] == "bands" && parts.Length == 3 && parts[1] == "authored" && BandNames.TryGetValue(parts[2], out var authoredBand))
        {
            label = "Questions authored as " + authoredBand;
            return true;
        }

        // peer.<letter>.<key>: another model's own figure
        if (parts[0] == "peer" && parts.Length >= 3 && parts[1].Length > 0 && parts[1].All(char.IsAsciiLetterUpper))
        {
            string suffix = string.Join('.', parts.Skip(2));
            if (PeerLabels.TryGetValue(suffix, out var peerLabel))
            {
                label = string.Format(CultureInfo.InvariantCulture, peerLabel, "Model " + parts[1]);
                return true;
            }

            if (TryLabel(suffix, out var inner))
            {
                label = "Model " + parts[1] + ": " + LowerFirst(inner);
                return true;
            }
        }

        return false;
    }

    private static string? Comparison(string suffix) => suffix switch
    {
        "peerMean" => " peer mean",
        "difference" => " difference from the peer mean",
        _ => null
    };

    /// <summary>"foo.barBaz" as "Foo bar baz".</summary>
    private static string Fallback(string? key)
    {
        var words = new List<string>();
        foreach (string part in (key ?? string.Empty).Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var word = new StringBuilder();
            for (int i = 0; i < part.Length; i++)
            {
                char c = part[i];
                bool boundary = i > 0 && char.IsUpper(c) && (char.IsLower(part[i - 1]) || char.IsDigit(part[i - 1]));
                if (boundary)
                {
                    words.Add(word.ToString());
                    word.Clear();
                }
                word.Append(c);
            }
            if (word.Length > 0) words.Add(word.ToString());
        }

        if (words.Count == 0) return "Figure";

        string text = string.Join(" ", words.Select(w => w.All(char.IsUpper) && w.Length > 1 ? w : w.ToLowerInvariant()));
        return char.ToUpper(text[0], CultureInfo.InvariantCulture) + text[1..];
    }

    private static string LowerFirst(string text)
        => text.Length > 1 && char.IsUpper(text[0]) && !char.IsUpper(text[1])
            ? char.ToLowerInvariant(text[0]) + text[1..]
            : text;
}
