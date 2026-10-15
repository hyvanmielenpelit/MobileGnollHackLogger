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

    // Values 2 to 7 and 9 to 12 were the anchors of two retired audiences and are not reused.

    /// <summary>Comparison-scope Executive Summary: How they compare, after its tables.</summary>
    HowTheyCompare = 8,

    /// <summary>Chat consistency documents: Results by endpoint, after the verdict table.</summary>
    ChatConsistencyResults = 13,

    /// <summary>Chat consistency documents: Overseer events, after the events table.</summary>
    ChatConsistencyEvents = 14,

    /// <summary>Provider availability: in the Executive Summary, after the Key figures.</summary>
    ProviderAvailability = 15,

    /// <summary>Root Cause Analysis: Evidence: intelligence, after its tables.</summary>
    RcaIntelligence = 16,

    /// <summary>Root Cause Analysis: Evidence: speed, after its tables.</summary>
    RcaSpeed = 17,

    /// <summary>Root Cause Analysis: Evidence: cost, after its tables.</summary>
    RcaCost = 18,

    /// <summary>Root Cause Analysis: Hypotheses, after the hypothesis tables.</summary>
    RcaHypotheses = 19,
}

/// <summary>
/// The chart figures a report-pack document can carry, and where each is drawn per audience. The
/// renderer writes one <c>[[figure:&lt;key&gt;]]</c> line at an anchor for each chart it is given;
/// the PDF and Word writers draw the chart there. Several figures at one anchor keep the order of
/// <see cref="FigureKeys"/>, then <see cref="ServerFigureKeys"/>, or of <see cref="ChatConsistencyFigureKeys"/>
/// in a chat consistency document. The browser draws the first two lists; Overseer draws the
/// <see cref="ServerFigureKeys"/> itself, for the Root Cause Analysis only.
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
    public const string AvailabilityKey = "a1-availability";

    // The chat consistency figures: the model under test over the two periods, drawn by the analysis wizard.
    public const string ChatQualityKey = "cc1-quality";
    public const string ChatSpeedKey = "cc2-speed";
    public const string ChatWorkKey = "cc3-work";
    public const string ChatTimelineKey = "cc4-timeline";
    public const string ChatAvailabilityKey = "cc5-availability";

    // The Root Cause Analysis figures, drawn by Overseer from the stored root-cause result.
    public const string RcaPointsAtRiskKey = "r1-points-at-risk";
    public const string RcaSpeedKey = "r2-speed-decomposition";
    public const string RcaCostKey = "r3-cost-decomposition";
    public const string RcaHypothesesKey = "r4-hypothesis-confidence";

    public const string MarkerPrefix = "[[figure:";
    public const string MarkerSuffix = "]]";

    /// <summary>The model comparison's figure keys, in placement order.</summary>
    public static readonly IReadOnlyList<string> FigureKeys = new[]
    {
        QualityKey, SpeedKey, CostKey, ProfileKey, QualitySpeedKey, QualityCostKey, SpeedCostKey, AvailabilityKey
    };

    /// <summary>The chat consistency figure keys, in placement order.</summary>
    public static readonly IReadOnlyList<string> ChatConsistencyFigureKeys = new[]
    {
        ChatQualityKey, ChatSpeedKey, ChatWorkKey, ChatTimelineKey, ChatAvailabilityKey
    };

    /// <summary>The server-drawn figure keys of the Root Cause Analysis, in placement order.</summary>
    public static readonly IReadOnlyList<string> ServerFigureKeys = new[]
    {
        RcaPointsAtRiskKey, RcaSpeedKey, RcaCostKey, RcaHypothesesKey
    };

    /// <summary>
    /// Every known figure key: <see cref="FigureKeys"/>, then <see cref="ChatConsistencyFigureKeys"/>, then
    /// <see cref="ServerFigureKeys"/>.
    /// </summary>
    public static readonly IReadOnlyList<string> AllFigureKeys = FigureKeys.Concat(ChatConsistencyFigureKeys).Concat(ServerFigureKeys).ToArray();

    // The anchor of each server-drawn figure in a Root Cause Analysis of either scope, in the order of
    // ServerFigureKeys: points at risk, speed, cost and hypothesis confidence, each in its own section.
    private static readonly BenchmarkReportChartAnchor[] RootCauseAnchors =
    {
        BenchmarkReportChartAnchor.RcaIntelligence, BenchmarkReportChartAnchor.RcaSpeed,
        BenchmarkReportChartAnchor.RcaCost, BenchmarkReportChartAnchor.RcaHypotheses
    };

    // The anchor of each figure per audience in a model-scope document, in the order of FigureKeys. The
    // Executive Summary draws every figure, provider availability included. Null where the audience
    // does not draw the figure.
    private static readonly IReadOnlyDictionary<BenchmarkReportAudience, BenchmarkReportChartAnchor?[]> ModelAnchors =
        new Dictionary<BenchmarkReportAudience, BenchmarkReportChartAnchor?[]>
        {
            [BenchmarkReportAudience.ExecutiveSummary] = Enumerable.Repeat((BenchmarkReportChartAnchor?)BenchmarkReportChartAnchor.HowItCompares, 7)
                .Append(BenchmarkReportChartAnchor.ProviderAvailability).ToArray(),
            [BenchmarkReportAudience.RootCauseAnalysis] = RootCauseBrowserAnchors(),
        };

    // The anchor of each figure per audience in a comparison-scope document, in the order of FigureKeys.
    // Null where the audience does not draw the figure: the Executive Summary has no availability block.
    private static readonly IReadOnlyDictionary<BenchmarkReportAudience, BenchmarkReportChartAnchor?[]> ComparisonAnchors =
        new Dictionary<BenchmarkReportAudience, BenchmarkReportChartAnchor?[]>
        {
            [BenchmarkReportAudience.ExecutiveSummary] = Enumerable.Repeat((BenchmarkReportChartAnchor?)BenchmarkReportChartAnchor.HowTheyCompare, 7)
                .Append(null).ToArray(),
            [BenchmarkReportAudience.RootCauseAnalysis] = RootCauseBrowserAnchors(),
        };

    // The browser figures a Root Cause Analysis draws where peers exist, in the order of FigureKeys:
    // intelligence in its intelligence evidence, speed and intelligence against speed in its speed
    // evidence, cost and intelligence against cost in its cost evidence. It draws no profile, speed
    // against cost or availability figure.
    private static BenchmarkReportChartAnchor?[] RootCauseBrowserAnchors() => new BenchmarkReportChartAnchor?[]
    {
        BenchmarkReportChartAnchor.RcaIntelligence, BenchmarkReportChartAnchor.RcaSpeed, BenchmarkReportChartAnchor.RcaCost,
        null,
        BenchmarkReportChartAnchor.RcaSpeed, BenchmarkReportChartAnchor.RcaCost,
        null, null
    };

    // The anchor of each chat consistency figure per audience, in the order of ChatConsistencyFigureKeys.
    // Every audience may draw every figure the client chose for it: quality, speed, work and provider
    // availability after the verdict table, the timeline after the events table, and in the Executive
    // Summary, which prints no events table, after the verdict table too.
    private static readonly IReadOnlyDictionary<BenchmarkReportAudience, BenchmarkReportChartAnchor?[]> ChatConsistencyAnchors =
        new Dictionary<BenchmarkReportAudience, BenchmarkReportChartAnchor?[]>
        {
            [BenchmarkReportAudience.ExecutiveSummary] = new BenchmarkReportChartAnchor?[]
            {
                BenchmarkReportChartAnchor.ChatConsistencyResults, BenchmarkReportChartAnchor.ChatConsistencyResults,
                BenchmarkReportChartAnchor.ChatConsistencyResults, BenchmarkReportChartAnchor.ChatConsistencyResults,
                BenchmarkReportChartAnchor.ChatConsistencyResults
            },
            [BenchmarkReportAudience.ProviderIssueReport] = ChatResultsThenEvents(),
        };

    // The per-audience placement of a chat consistency document whose stored chart set has no layout:
    // every chart set uploaded before the client chose figures per document. Null where the audience
    // does not draw the figure; no legacy chart set holds the provider availability figure.
    private static readonly IReadOnlyDictionary<BenchmarkReportAudience, BenchmarkReportChartAnchor?[]> LegacyChatConsistencyAnchors =
        new Dictionary<BenchmarkReportAudience, BenchmarkReportChartAnchor?[]>
        {
            [BenchmarkReportAudience.ExecutiveSummary] = new BenchmarkReportChartAnchor?[]
            {
                BenchmarkReportChartAnchor.ChatConsistencyResults, BenchmarkReportChartAnchor.ChatConsistencyResults, null, null, null
            },
            [BenchmarkReportAudience.ProviderIssueReport] = new BenchmarkReportChartAnchor?[]
            {
                null, BenchmarkReportChartAnchor.ChatConsistencyResults, null, BenchmarkReportChartAnchor.ChatConsistencyEvents, null
            },
        };

    private static BenchmarkReportChartAnchor?[] ChatResultsThenEvents() => new BenchmarkReportChartAnchor?[]
    {
        BenchmarkReportChartAnchor.ChatConsistencyResults, BenchmarkReportChartAnchor.ChatConsistencyResults,
        BenchmarkReportChartAnchor.ChatConsistencyResults, BenchmarkReportChartAnchor.ChatConsistencyEvents,
        BenchmarkReportChartAnchor.ChatConsistencyResults
    };

    /// <summary>Whether <paramref name="figureKey"/> is one of <see cref="AllFigureKeys"/>, compared ordinally.</summary>
    public static bool IsKnown(string? figureKey)
        => figureKey != null && AllFigureKeys.Contains(figureKey, StringComparer.Ordinal);

    /// <summary>Whether <paramref name="figureKey"/> is one of <see cref="ServerFigureKeys"/>, compared ordinally.</summary>
    public static bool IsServerDrawn(string? figureKey)
        => figureKey != null && IndexOf(ServerFigureKeys, figureKey) >= 0;

    /// <summary>Whether a document of <paramref name="audience"/> and <paramref name="scope"/> carries the server-drawn figures.</summary>
    private static bool DrawsServerFigures(BenchmarkReportAudience audience, BenchmarkReportScope scope)
        => audience == BenchmarkReportAudience.RootCauseAnalysis && scope != BenchmarkReportScope.ChatConsistency;

    /// <summary>Where a figure is drawn in a document of <paramref name="audience"/>; null for an unknown key or audience.</summary>
    public static BenchmarkReportChartAnchor? AnchorOf(BenchmarkReportAudience audience, string? figureKey)
        => AnchorOf(audience, figureKey, BenchmarkReportScope.Model);

    /// <summary>
    /// Where a figure is drawn in a document of <paramref name="audience"/> and <paramref name="scope"/>;
    /// null for an unknown key or audience, and for a figure the scope or audience does not draw.
    /// <paramref name="legacy"/> reads a chat consistency document's placement from its legacy table,
    /// for a chart set stored without a layout; other scopes have one table. A server-drawn figure is
    /// placed by <see cref="RootCauseAnchors"/> in a Root Cause Analysis of either scope.
    /// </summary>
    public static BenchmarkReportChartAnchor? AnchorOf(
        BenchmarkReportAudience audience, string? figureKey, BenchmarkReportScope scope, bool legacy = false)
    {
        if (figureKey == null) return null;
        int serverIndex = IndexOf(ServerFigureKeys, figureKey);
        if (serverIndex >= 0)
        {
            return DrawsServerFigures(audience, scope) ? RootCauseAnchors[serverIndex] : null;
        }

        var (keys, table) = TableOf(scope, legacy);
        int index = IndexOf(keys, figureKey);
        if (index < 0 || !table.TryGetValue(audience, out var anchors)) return null;
        return anchors[index];
    }

    /// <summary>The figure keys drawn at <paramref name="anchor"/> in a document of <paramref name="audience"/>, in placement order.</summary>
    public static IReadOnlyList<string> KeysAt(BenchmarkReportAudience audience, BenchmarkReportChartAnchor anchor)
        => KeysAt(audience, anchor, BenchmarkReportScope.Model);

    /// <summary>
    /// The figure keys drawn at <paramref name="anchor"/> in a document of <paramref name="audience"/> and
    /// <paramref name="scope"/>, in placement order; <paramref name="legacy"/> as in <see cref="AnchorOf(BenchmarkReportAudience, string?, BenchmarkReportScope, bool)"/>.
    /// At a Root Cause Analysis anchor the browser figures come first, then the server-drawn figure placed there.
    /// </summary>
    public static IReadOnlyList<string> KeysAt(
        BenchmarkReportAudience audience, BenchmarkReportChartAnchor anchor, BenchmarkReportScope scope, bool legacy = false)
    {
        var (keys, table) = TableOf(scope, legacy);
        var result = table.TryGetValue(audience, out var anchors)
            ? keys.Where((_, i) => anchors[i] == anchor).ToList()
            : new List<string>();
        if (DrawsServerFigures(audience, scope))
        {
            result.AddRange(ServerFigureKeys.Where((_, i) => RootCauseAnchors[i] == anchor));
        }
        return result;
    }

    /// <summary>The browser-drawn figure keys a document of <paramref name="scope"/> can carry, and their anchors per audience.</summary>
    private static (IReadOnlyList<string> Keys, IReadOnlyDictionary<BenchmarkReportAudience, BenchmarkReportChartAnchor?[]> Table) TableOf(
        BenchmarkReportScope scope, bool legacy)
        => scope switch
        {
            BenchmarkReportScope.ChatConsistency => (ChatConsistencyFigureKeys, legacy ? LegacyChatConsistencyAnchors : ChatConsistencyAnchors),
            BenchmarkReportScope.Comparison => (FigureKeys, ComparisonAnchors),
            _ => (FigureKeys, ModelAnchors)
        };

    /// <summary>The marker line of a figure: <c>[[figure:p1a-quality]]</c>.</summary>
    public static string Marker(string figureKey) => MarkerPrefix + figureKey + MarkerSuffix;

    private static int IndexOf(IReadOnlyList<string> keys, string figureKey)
    {
        for (int i = 0; i < keys.Count; i++)
        {
            if (string.Equals(keys[i], figureKey, StringComparison.Ordinal)) return i;
        }
        return -1;
    }
}
