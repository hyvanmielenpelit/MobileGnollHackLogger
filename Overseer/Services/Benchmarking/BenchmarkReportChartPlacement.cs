namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using MobileGnollHackLogger.Data;

/// <summary>A place in a report-pack document where chart figures may be drawn.</summary>
public enum BenchmarkReportChartAnchor
{
    /// <summary>Executive Summary: How it compares, after its tables.</summary>
    HowItCompares = 1,

    /// <summary>Report for AI Researchers and Developers: the end of Results against peers → Quality.</summary>
    QualityResults = 2,

    /// <summary>Report for AI Researchers and Developers: the end of Results against peers → Speed.</summary>
    SpeedResults = 3,

    /// <summary>Report for AI Researchers and Developers: the end of Results against peers → Cost.</summary>
    CostResults = 4,

    /// <summary>Report for AI Researchers and Developers: the end of Results against peers.</summary>
    ResultsAgainstPeersEnd = 5,

    /// <summary>Report for AI Researchers and Developers: Speed and cost, after its table.</summary>
    SpeedAndCost = 6,

    /// <summary>Internal Improvement Brief: section 3, after its key figures.</summary>
    ModelResult = 7,

    /// <summary>Comparison-scope Executive Summary: How they compare, after its tables.</summary>
    HowTheyCompare = 8,

    /// <summary>Comparison-scope Report for AI Researchers and Developers: Results, after the results table.</summary>
    ComparisonResults = 9,

    /// <summary>Comparison-scope Report for AI Researchers and Developers: Dimension profiles, after its table.</summary>
    DimensionProfiles = 10,

    /// <summary>Comparison-scope Report for AI Researchers and Developers: Speed and cost frontier, after its table.</summary>
    SpeedAndCostFrontier = 11,

    /// <summary>Comparison-scope Internal Improvement Brief: Models compared, after its table.</summary>
    ModelsCompared = 12,

    /// <summary>Chat consistency documents: Results by endpoint, after the verdict table.</summary>
    ChatConsistencyResults = 13,

    /// <summary>Chat consistency documents: Overseer events, after the events table.</summary>
    ChatConsistencyEvents = 14,
}

/// <summary>
/// The chart figures a report-pack document can carry, and where each is drawn per audience. The
/// renderer writes one <c>[[figure:&lt;key&gt;]]</c> line at an anchor for each chart it is given;
/// the PDF and Word writers draw the chart there. Several figures at one anchor keep the order of
/// <see cref="FigureKeys"/>, or of <see cref="ChatConsistencyFigureKeys"/> in a chat consistency document.
/// </summary>
public static class BenchmarkReportChartPlacement
{
    public const string QualityKey = "p1a-quality";
    public const string SpeedKey = "p1b-speed";
    public const string CostKey = "p1c-cost";
    public const string ProfileKey = "p2-profile";
    public const string QualitySpeedKey = "s1-quality-speed";
    public const string QualityCostKey = "s2-quality-cost";
    public const string SpeedCostKey = "s3-speed-cost";

    // The chat consistency figures: the model under test over the two periods, drawn by the analysis wizard.
    public const string ChatQualityKey = "cc1-quality";
    public const string ChatSpeedKey = "cc2-speed";
    public const string ChatWorkKey = "cc3-work";
    public const string ChatTimelineKey = "cc4-timeline";

    public const string MarkerPrefix = "[[figure:";
    public const string MarkerSuffix = "]]";

    /// <summary>The model comparison's figure keys, in placement order.</summary>
    public static readonly IReadOnlyList<string> FigureKeys = new[]
    {
        QualityKey, SpeedKey, CostKey, ProfileKey, QualitySpeedKey, QualityCostKey, SpeedCostKey
    };

    /// <summary>The chat consistency figure keys, in placement order.</summary>
    public static readonly IReadOnlyList<string> ChatConsistencyFigureKeys = new[]
    {
        ChatQualityKey, ChatSpeedKey, ChatWorkKey, ChatTimelineKey
    };

    /// <summary>Every known figure key: <see cref="FigureKeys"/>, then <see cref="ChatConsistencyFigureKeys"/>.</summary>
    public static readonly IReadOnlyList<string> AllFigureKeys = FigureKeys.Concat(ChatConsistencyFigureKeys).ToArray();

    // The anchor of each figure per audience, in the order of FigureKeys.
    private static readonly IReadOnlyDictionary<BenchmarkReportAudience, BenchmarkReportChartAnchor[]> Anchors =
        new Dictionary<BenchmarkReportAudience, BenchmarkReportChartAnchor[]>
        {
            [BenchmarkReportAudience.ExecutiveSummary] = Enumerable.Repeat(BenchmarkReportChartAnchor.HowItCompares, 7).ToArray(),
            [BenchmarkReportAudience.TechnicalReport] = new[]
            {
                BenchmarkReportChartAnchor.QualityResults,
                BenchmarkReportChartAnchor.SpeedResults,
                BenchmarkReportChartAnchor.CostResults,
                BenchmarkReportChartAnchor.ResultsAgainstPeersEnd,
                BenchmarkReportChartAnchor.SpeedAndCost,
                BenchmarkReportChartAnchor.SpeedAndCost,
                BenchmarkReportChartAnchor.SpeedAndCost
            },
            [BenchmarkReportAudience.InternalBrief] = Enumerable.Repeat(BenchmarkReportChartAnchor.ModelResult, 7).ToArray(),
        };

    // The anchor of each figure per audience in a comparison-scope document, in the order of FigureKeys.
    private static readonly IReadOnlyDictionary<BenchmarkReportAudience, BenchmarkReportChartAnchor[]> ComparisonAnchors =
        new Dictionary<BenchmarkReportAudience, BenchmarkReportChartAnchor[]>
        {
            [BenchmarkReportAudience.ExecutiveSummary] = Enumerable.Repeat(BenchmarkReportChartAnchor.HowTheyCompare, 7).ToArray(),
            [BenchmarkReportAudience.TechnicalReport] = new[]
            {
                BenchmarkReportChartAnchor.ComparisonResults,
                BenchmarkReportChartAnchor.SpeedAndCostFrontier,
                BenchmarkReportChartAnchor.SpeedAndCostFrontier,
                BenchmarkReportChartAnchor.DimensionProfiles,
                BenchmarkReportChartAnchor.SpeedAndCostFrontier,
                BenchmarkReportChartAnchor.SpeedAndCostFrontier,
                BenchmarkReportChartAnchor.SpeedAndCostFrontier
            },
            [BenchmarkReportAudience.InternalBrief] = Enumerable.Repeat(BenchmarkReportChartAnchor.ModelsCompared, 7).ToArray(),
        };

    // The anchor of each chat consistency figure per audience, in the order of ChatConsistencyFigureKeys;
    // null where the audience does not draw it.
    private static readonly IReadOnlyDictionary<BenchmarkReportAudience, BenchmarkReportChartAnchor?[]> ChatConsistencyAnchors =
        new Dictionary<BenchmarkReportAudience, BenchmarkReportChartAnchor?[]>
        {
            [BenchmarkReportAudience.ExecutiveSummary] = new BenchmarkReportChartAnchor?[]
            {
                BenchmarkReportChartAnchor.ChatConsistencyResults, BenchmarkReportChartAnchor.ChatConsistencyResults, null, null
            },
            [BenchmarkReportAudience.TechnicalReport] = new BenchmarkReportChartAnchor?[]
            {
                BenchmarkReportChartAnchor.ChatConsistencyResults, BenchmarkReportChartAnchor.ChatConsistencyResults,
                BenchmarkReportChartAnchor.ChatConsistencyResults, BenchmarkReportChartAnchor.ChatConsistencyEvents
            },
            [BenchmarkReportAudience.InternalBrief] = new BenchmarkReportChartAnchor?[]
            {
                BenchmarkReportChartAnchor.ChatConsistencyResults, null, BenchmarkReportChartAnchor.ChatConsistencyResults, null
            },
            [BenchmarkReportAudience.ProviderIssueReport] = new BenchmarkReportChartAnchor?[]
            {
                null, BenchmarkReportChartAnchor.ChatConsistencyResults, null, BenchmarkReportChartAnchor.ChatConsistencyEvents
            },
        };

    /// <summary>Whether <paramref name="figureKey"/> is one of <see cref="AllFigureKeys"/>, compared ordinally.</summary>
    public static bool IsKnown(string? figureKey)
        => figureKey != null && AllFigureKeys.Contains(figureKey, StringComparer.Ordinal);

    /// <summary>Where a figure is drawn in a document of <paramref name="audience"/>; null for an unknown key or audience.</summary>
    public static BenchmarkReportChartAnchor? AnchorOf(BenchmarkReportAudience audience, string? figureKey)
        => AnchorOf(audience, figureKey, BenchmarkReportScope.Model);

    /// <summary>
    /// Where a figure is drawn in a document of <paramref name="audience"/> and <paramref name="scope"/>;
    /// null for an unknown key or audience, and for a figure the scope or audience does not draw.
    /// </summary>
    public static BenchmarkReportChartAnchor? AnchorOf(BenchmarkReportAudience audience, string? figureKey, BenchmarkReportScope scope)
    {
        if (figureKey == null) return null;
        var (keys, table) = TableOf(scope);
        int index = IndexOf(keys, figureKey);
        if (index < 0 || !table.TryGetValue(audience, out var anchors)) return null;
        return anchors[index];
    }

    /// <summary>The figure keys drawn at <paramref name="anchor"/> in a document of <paramref name="audience"/>, in placement order.</summary>
    public static IReadOnlyList<string> KeysAt(BenchmarkReportAudience audience, BenchmarkReportChartAnchor anchor)
        => KeysAt(audience, anchor, BenchmarkReportScope.Model);

    /// <summary>The figure keys drawn at <paramref name="anchor"/> in a document of <paramref name="audience"/> and <paramref name="scope"/>, in placement order.</summary>
    public static IReadOnlyList<string> KeysAt(BenchmarkReportAudience audience, BenchmarkReportChartAnchor anchor, BenchmarkReportScope scope)
    {
        var (keys, table) = TableOf(scope);
        if (!table.TryGetValue(audience, out var anchors)) return Array.Empty<string>();
        return keys.Where((_, i) => anchors[i] == anchor).ToList();
    }

    /// <summary>The figure keys a document of <paramref name="scope"/> can carry, and their anchors per audience.</summary>
    private static (IReadOnlyList<string> Keys, IReadOnlyDictionary<BenchmarkReportAudience, BenchmarkReportChartAnchor?[]> Table) TableOf(BenchmarkReportScope scope)
        => scope switch
        {
            BenchmarkReportScope.ChatConsistency => (ChatConsistencyFigureKeys, ChatConsistencyAnchors),
            BenchmarkReportScope.Comparison => (FigureKeys, NullableComparisonAnchors),
            _ => (FigureKeys, NullableAnchors)
        };

    private static readonly IReadOnlyDictionary<BenchmarkReportAudience, BenchmarkReportChartAnchor?[]> NullableAnchors = Nullable(Anchors);
    private static readonly IReadOnlyDictionary<BenchmarkReportAudience, BenchmarkReportChartAnchor?[]> NullableComparisonAnchors = Nullable(ComparisonAnchors);

    private static IReadOnlyDictionary<BenchmarkReportAudience, BenchmarkReportChartAnchor?[]> Nullable(
        IReadOnlyDictionary<BenchmarkReportAudience, BenchmarkReportChartAnchor[]> table)
        => table.ToDictionary(e => e.Key, e => e.Value.Select(a => (BenchmarkReportChartAnchor?)a).ToArray());

    /// <summary>The marker line of a figure: <c>[[figure:p1a-quality]]</c>.</summary>
    public static string Marker(string figureKey) => MarkerPrefix + figureKey + MarkerSuffix;

    /// <summary>
    /// Whether <paramref name="line"/>, trimmed, is exactly the marker of a known figure; its key in
    /// <paramref name="figureKey"/>, empty otherwise.
    /// </summary>
    public static bool TryParseMarker(string? line, out string figureKey)
    {
        figureKey = string.Empty;
        string text = line?.Trim() ?? string.Empty;
        if (!text.StartsWith(MarkerPrefix, StringComparison.Ordinal) || !text.EndsWith(MarkerSuffix, StringComparison.Ordinal)) return false;
        if (text.Length <= MarkerPrefix.Length + MarkerSuffix.Length) return false;

        string key = text[MarkerPrefix.Length..^MarkerSuffix.Length];
        if (!IsKnown(key)) return false;

        figureKey = key;
        return true;
    }

    private static int IndexOf(IReadOnlyList<string> keys, string figureKey)
    {
        for (int i = 0; i < keys.Count; i++)
        {
            if (string.Equals(keys[i], figureKey, StringComparison.Ordinal)) return i;
        }
        return -1;
    }
}
