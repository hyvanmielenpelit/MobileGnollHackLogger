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
}

/// <summary>
/// The chart figures a report-pack document can carry, and where each is drawn per audience. The
/// renderer writes one <c>[[figure:&lt;key&gt;]]</c> line at an anchor for each chart it is given;
/// the PDF and Word writers draw the chart there. Several figures at one anchor keep the order of
/// <see cref="FigureKeys"/>.
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

    public const string MarkerPrefix = "[[figure:";
    public const string MarkerSuffix = "]]";

    /// <summary>Every figure key, in placement order.</summary>
    public static readonly IReadOnlyList<string> FigureKeys = new[]
    {
        QualityKey, SpeedKey, CostKey, ProfileKey, QualitySpeedKey, QualityCostKey, SpeedCostKey
    };

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

    /// <summary>Whether <paramref name="figureKey"/> is one of <see cref="FigureKeys"/>, compared ordinally.</summary>
    public static bool IsKnown(string? figureKey)
        => figureKey != null && FigureKeys.Contains(figureKey, StringComparer.Ordinal);

    /// <summary>Where a figure is drawn in a document of <paramref name="audience"/>; null for an unknown key or audience.</summary>
    public static BenchmarkReportChartAnchor? AnchorOf(BenchmarkReportAudience audience, string? figureKey)
        => AnchorOf(audience, figureKey, BenchmarkReportScope.Model);

    /// <summary>Where a figure is drawn in a document of <paramref name="audience"/> and <paramref name="scope"/>; null for an unknown key or audience.</summary>
    public static BenchmarkReportChartAnchor? AnchorOf(BenchmarkReportAudience audience, string? figureKey, BenchmarkReportScope scope)
    {
        if (!IsKnown(figureKey) || !TableOf(scope).TryGetValue(audience, out var anchors)) return null;
        return anchors[IndexOf(figureKey!)];
    }

    /// <summary>The figure keys drawn at <paramref name="anchor"/> in a document of <paramref name="audience"/>, in placement order.</summary>
    public static IReadOnlyList<string> KeysAt(BenchmarkReportAudience audience, BenchmarkReportChartAnchor anchor)
        => KeysAt(audience, anchor, BenchmarkReportScope.Model);

    /// <summary>The figure keys drawn at <paramref name="anchor"/> in a document of <paramref name="audience"/> and <paramref name="scope"/>, in placement order.</summary>
    public static IReadOnlyList<string> KeysAt(BenchmarkReportAudience audience, BenchmarkReportChartAnchor anchor, BenchmarkReportScope scope)
    {
        if (!TableOf(scope).TryGetValue(audience, out var anchors)) return Array.Empty<string>();
        return FigureKeys.Where((_, i) => anchors[i] == anchor).ToList();
    }

    private static IReadOnlyDictionary<BenchmarkReportAudience, BenchmarkReportChartAnchor[]> TableOf(BenchmarkReportScope scope)
        => scope == BenchmarkReportScope.Comparison ? ComparisonAnchors : Anchors;

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

    private static int IndexOf(string figureKey)
    {
        for (int i = 0; i < FigureKeys.Count; i++)
        {
            if (string.Equals(FigureKeys[i], figureKey, StringComparison.Ordinal)) return i;
        }
        return -1;
    }
}
