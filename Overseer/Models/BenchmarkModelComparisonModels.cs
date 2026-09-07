namespace Overseer.Models;

using System;
using System.Collections.Generic;

// DTOs for the cross-model comparison view: one point per candidate model, gated by
// BenchmarkCrossModelComparability and measured through BenchmarkGroupStatistics.
//
// They live in their own file for the same reason the multi-run DTOs do — the contract is large
// enough to be worth reading in one place — and they are deliberately *flat*: unlike
// BenchmarkGroupAnalysisDto, which passes the statistics record through untouched, a comparison
// entry carries only the three axes it is allowed to be plotted on. That narrowing is the point.
// An excluded entry has no measures at all, so a chart cannot render one by ignoring a flag.

/// <summary>
/// Which price card every entry's candidate cost is computed from.
///
/// <para>Costs from different snapshot dates are not comparable: two runs months apart were priced
/// from different catalogs, and a catalog carries scheduled changes. Plotting the stored figures
/// side by side compares prices, not models. Token counts are the invariant, so re-pricing is
/// arithmetic over the totals already stored on the run and never needs a re-run.</para>
/// </summary>
public enum BenchmarkModelComparisonPricingBasis
{
    /// <summary>
    /// Each entry's own stored pricing snapshot. Honest about what was actually spent, and
    /// <b>not</b> comparable across dates — the cost axis is flagged degraded whenever the
    /// snapshots differ.
    /// </summary>
    AsRun = 0,

    /// <summary>
    /// Today's catalog for every entry. Comparable, and equal to what was spent only for a run
    /// priced from today's card. The default.
    /// </summary>
    Current = 1
}

/// <summary>
/// What to compare. Each run id and each group id becomes one point; a group is one point over its
/// members, never several.
/// </summary>
public class BenchmarkModelComparisonRequest
{
    /// <summary>Single runs, each contributing a point at <i>R</i> = 1.</summary>
    public List<long> RunIds { get; set; } = new();

    /// <summary>
    /// Analysis groups, each contributing one point over its members. A group whose own members are
    /// not poolable is returned excluded rather than pooled.
    /// </summary>
    public List<long> GroupIds { get; set; } = new();

    public BenchmarkModelComparisonPricingBasis PricingBasis { get; set; }
        = BenchmarkModelComparisonPricingBasis.Current;
}

/// <summary>
/// The quality axis: the Intelligence Index with its 95 % interval.
///
/// <para>Raw Quality Index is deliberately absent. It ignores the critical-error cap, which is the
/// failure mode that matters most, so a model that fabricates confidently would read as its equal
/// on this axis.</para>
/// </summary>
public class BenchmarkModelComparisonQualityDto
{
    public double PointEstimate { get; set; }

    /// <summary>Items behind the estimate.</summary>
    public int ItemCount { get; set; }

    /// <summary>
    /// The combined half-width: item sampling alone at <i>R</i> &lt; 3, item sampling and
    /// reproducibility in quadrature above it. Null only when neither component could be computed.
    /// </summary>
    public double? IntervalHalfWidth { get; set; }

    public double? IntervalLower { get; set; }
    public double? IntervalUpper { get; set; }

    /// <summary>A bound hit the 0–100 score range, so the rendered interval is narrower than the half-width.</summary>
    public bool IntervalTruncated { get; set; }

    /// <summary>
    /// The item-sampling component: <i>would a different draw of questions move this?</i> It does
    /// not shrink with more runs.
    /// </summary>
    public double? ItemSamplingHalfWidth { get; set; }

    /// <summary>
    /// The reproducibility component: <i>would a re-run move this?</i> Null below three runs, where
    /// a standard deviation over runs is arithmetic rather than evidence.
    /// </summary>
    public double? ReproducibilityHalfWidth { get; set; }

    /// <inheritdoc cref="ReproducibilityHalfWidth"/>
    public double? ReproducibilityStandardDeviation { get; set; }

    /// <summary>False below three runs, where the interval covers one source of variation, not two.</summary>
    public bool ReproducibilityAvailable { get; set; }

    /// <summary>Ready to render under the interval: which sources it actually covers.</summary>
    public string IntervalBasis { get; set; } = string.Empty;
}

/// <summary>
/// The speed axis: time to first token, which is the latency a chat user actually perceives. Model
/// time is carried beside it as a secondary column, never as the axis.
/// </summary>
public class BenchmarkModelComparisonSpeedDto
{
    public double? TtftP50Ms { get; set; }

    /// <summary>The upper whisker.</summary>
    public double? TtftP90Ms { get; set; }

    /// <summary>Answers that reported a time to first token. Its own denominator; see the model-time count.</summary>
    public int TtftAnswerCount { get; set; }

    public double? ModelTimeP50Ms { get; set; }
    public double? ModelTimeP90Ms { get; set; }
    public int PooledAnswerCount { get; set; }

    /// <summary>The plotted set mixes timing conditions.</summary>
    public bool Degraded { get; set; }

    public string? DegradedReason { get; set; }

    /// <summary>The standing caveat for these figures, from the group statistics.</summary>
    public string Caveat { get; set; } = string.Empty;
}

/// <summary>
/// The cost axis: candidate spend per question, in USD.
///
/// <para>Candidate-only, because grading roles are most of a run's cost and grading spend is not
/// transferable to the chat assistant. Per question rather than per run, because run cost scales
/// with suite size and would compare suites instead of models.</para>
/// </summary>
public class BenchmarkModelComparisonCostDto
{
    /// <summary>
    /// Null unless a price card was resolved for <b>every</b> run behind the entry. A total over the
    /// priced subset would understate the spend without saying so, and an unknown cost is reported
    /// as unknown, never as zero.
    /// </summary>
    public double? CandidateCostPerQuestionUsd { get; set; }

    public double? CandidateCostPerRunUsd { get; set; }
    public double? CandidateTotalCostUsd { get; set; }

    /// <summary>`AsRun` or `Current`, echoing the basis the whole comparison was computed on.</summary>
    public string Basis { get; set; } = string.Empty;

    /// <summary>The price card's own `asOf`, where the catalog or the snapshot published one.</summary>
    public string? PricingAsOf { get; set; }

    /// <summary>A price card was resolved for every run behind this entry.</summary>
    public bool PricingResolved { get; set; }

    public bool Degraded { get; set; }

    public string? DegradedReason { get; set; }

    /// <summary>
    /// The catalog announces a price change for this model, as `yyyy-MM-dd`. Surfaced when it falls
    /// inside the next twelve months: a cost ranking that flips on a known future date is a
    /// conclusion with an expiry date on it.
    /// </summary>
    public string? ScheduledChangeEffectiveFrom { get; set; }

    public string? ScheduledChangeNote { get; set; }
}

/// <summary>
/// Figures that belong in the table and in no chart, each for a stated reason.
///
/// <para>They are grouped here rather than scattered through the entry so that "this is not an
/// axis" is a property of where a number lives, not a convention someone has to remember.</para>
/// </summary>
public class BenchmarkModelComparisonTableDto
{
    /// <summary>Mean of the per-run Speed Indices. Saturated, and comparable only within one thinking level.</summary>
    public double? MeanSpeedIndex { get; set; }

    /// <summary>
    /// At least half the scored answers finished inside their difficulty-scaled target, so the index
    /// cannot discriminate at this speed. Rendering it as a bar would show several models tied at
    /// 100 that differ severalfold.
    /// </summary>
    public bool SpeedIndexSaturated { get; set; }

    public int SpeedIndexCeilingAnswerCount { get; set; }
    public int SpeedIndexScoredAnswerCount { get; set; }

    /// <summary>
    /// Mean run cost divided by the Intelligence Index. A ratio of two noisy estimators: no simple
    /// confidence interval, and its meaning inverts near zero.
    /// </summary>
    public double? CostPerIndexPointUsd { get; set; }

    /// <summary>Mean of the stored per-run Quality Indices, for cross-checking the axis figure only.</summary>
    public double? MeanStoredQualityIndex { get; set; }

    /// <summary>Items whose cross-run quality standard deviation reached the instability threshold.</summary>
    public int UnstableItemCount { get; set; }
}

/// <summary>One model, as configured, on one comparison.</summary>
public class BenchmarkModelComparisonEntryDto
{
    /// <summary>`run:12` or `group:3` — stable across a refresh, and what a chart keys its series by.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>`Run` or `Group`.</summary>
    public string SourceKind { get; set; } = string.Empty;

    public long SourceId { get; set; }

    /// <summary>The group's name; null for a single run.</summary>
    public string? SourceName { get; set; }

    public List<long> RunIds { get; set; } = new();

    /// <summary><i>R</i>: runs behind this point.</summary>
    public int RunCount { get; set; }

    public long? SuiteId { get; set; }
    public string? SuiteName { get; set; }

    // --- The model axis, which is what these points are allowed to differ on ---
    public string Provider { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string ModelDisplayName { get; set; } = string.Empty;
    public string? ThinkingLevel { get; set; }
    public string? ReasoningMode { get; set; }
    public string? ReasoningSummary { get; set; }
    public string? ServiceTier { get; set; }
    public int? MaxOutputTokens { get; set; }
    public string ParallelExecutionMode { get; set; } = string.Empty;

    /// <summary>A short axis label: the display name, plus the thinking level when the set mixes them.</summary>
    public string Label { get; set; } = string.Empty;

    public DateTime FirstRunStartedAtUtc { get; set; }
    public DateTime LastRunStartedAtUtc { get; set; }

    // --- The comparability verdict ---

    /// <summary>`Comparable`, `Degraded` or `Excluded`.</summary>
    public string State { get; set; } = string.Empty;

    public bool Comparable { get; set; }

    /// <summary>
    /// The entry measured something else. <see cref="Quality"/>, <see cref="Speed"/> and
    /// <see cref="Cost"/> are null on an excluded entry, so it cannot reach a chart at all.
    /// </summary>
    public bool Excluded { get; set; }

    public bool SpeedDegraded { get; set; }
    public bool CostDegraded { get; set; }

    /// <summary>The must-match keys this entry differs from the baseline on, by name.</summary>
    public List<string> ExcludingKeys { get; set; } = new();

    public List<string> SpeedDegradingKeys { get; set; } = new();
    public List<string> CostDegradingKeys { get; set; } = new();

    /// <summary>The differing keys with the baseline's value and this entry's, ready for the tier dialog.</summary>
    public List<BenchmarkComparabilityDifferenceDto> Differences { get; set; } = new();

    /// <summary>A sentence naming the state and exactly what moved.</summary>
    public string Explanation { get; set; } = string.Empty;

    // --- The measures. All three are null on an excluded entry. ---
    public BenchmarkModelComparisonQualityDto? Quality { get; set; }
    public BenchmarkModelComparisonSpeedDto? Speed { get; set; }
    public BenchmarkModelComparisonCostDto? Cost { get; set; }

    /// <summary>Table-only figures. Also null on an excluded entry.</summary>
    public BenchmarkModelComparisonTableDto? Table { get; set; }
}

/// <summary>
/// A cross-model comparison: one point per model, the pricing basis they were all costed on, and
/// the entries that may not be charted with the reason named.
/// </summary>
public class BenchmarkModelComparisonDto
{
    public string PricingBasis { get; set; } = string.Empty;

    /// <summary>Ready for the chart subtitle, including the date the basis was taken.</summary>
    public string PricingBasisLabel { get; set; } = string.Empty;

    public DateTime ComputedAtUtc { get; set; }

    public long? BaselineSuiteId { get; set; }
    public string? BaselineSuiteName { get; set; }

    /// <summary>The entries that define the baseline condition — those that may be charted.</summary>
    public List<string> BaselineEntryKeys { get; set; } = new();

    /// <summary>The baseline's value for every must-match key, so a report can print the condition.</summary>
    public Dictionary<string, string> BaselineKeyValues { get; set; } = new();

    /// <summary>The keys the points are allowed to differ on, for the view's own explanatory text.</summary>
    public List<string> ModelAxisKeys { get; set; } = new();

    public List<BenchmarkModelComparisonEntryDto> Entries { get; set; } = new();

    public int ComparableCount { get; set; }
    public int ExcludedCount { get; set; }

    /// <summary>The plotted models were configured at different thinking levels.</summary>
    public bool ThinkingLevelsDiffer { get; set; }

    /// <summary>The thinking-level caveat when <see cref="ThinkingLevelsDiffer"/>; null otherwise.</summary>
    public string? SpeedAxisCaveat { get; set; }

    /// <summary>A sentence describing the set: how many points may be charted, and how many may not.</summary>
    public string Explanation { get; set; } = string.Empty;

    /// <summary>
    /// Why Speed Index, cost per index point and pairwise significance are not axes. Carried as data
    /// so the view states the reasons rather than reinventing them — or quietly charting them.
    /// </summary>
    public List<BenchmarkModelComparisonExcludedMeasureDto> ExcludedMeasures { get; set; } = new();
}

/// <summary>A measure the comparison deliberately refuses to chart, and the reason.</summary>
public class BenchmarkModelComparisonExcludedMeasureDto
{
    public string Measure { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;

    /// <summary>Where the reader should look instead.</summary>
    public string Instead { get; set; } = string.Empty;
}
