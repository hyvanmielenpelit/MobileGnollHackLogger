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
/// What to compare. Each run id, group id and battery run id becomes one point; a group or a battery
/// result is one point over its members, never several. Battery results are compared only with
/// battery results.
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

    /// <summary>
    /// Battery runs, each contributing one point read from its latest battery analysis. A request
    /// naming any of them may name no run or group.
    /// </summary>
    public List<long> BatteryRunIds { get; set; } = new();

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

    /// <summary>Items behind the estimate: questions of the exam with at least one scored answer.</summary>
    public int ItemCount { get; set; }

    /// <summary>
    /// The questions these runs were asked, as their answers recorded them, whatever the suite holds
    /// now. Always <see cref="ItemCount"/> + <see cref="UnscoredItemCount"/>.
    /// </summary>
    public int ExamItemCount { get; set; }

    /// <summary>Questions asked with no answer that counts: failed, skipped, canceled or ungraded.</summary>
    public int UnscoredItemCount { get; set; }

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
/// The speed axis: the candidate's own model time, mean per answer and mean total per run. Time to
/// first token is carried beside it, because it is the latency a chat user actually perceives, but
/// it does not drive a total-cost-of-ownership figure the way model time does.
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

    /// <summary>Mean of the pooled per-answer model times.</summary>
    public double? ModelTimeMeanMs { get; set; }

    /// <summary>
    /// Mean over member runs of that run's own total model time over its Ok answers. For a battery
    /// result, per battery pass: the sum over suites of that mean.
    /// </summary>
    public double? TotalModelTimePerRunMeanMs { get; set; }

    /// <summary>Sample standard deviation of the per-run total model times. Null below two runs, and for a battery result.</summary>
    public double? TotalModelTimeSdMs { get; set; }

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
/// <para>The axis charted by default is candidate spend, because grading roles are most of a run's
/// cost and grading spend is not transferable to the chat assistant. Per question rather than per
/// run, because run cost scales with suite size and would compare suites instead of models.</para>
///
/// <para>The run total including every grading role is carried beside it as an opt-in measure. It
/// answers a different question — what one benchmark run of this model costs — and none of its
/// grading share transfers to the chat assistant.</para>
/// </summary>
public class BenchmarkModelComparisonCostDto
{
    /// <summary>
    /// Null unless a price card was resolved for <b>every</b> run behind the entry. A total over the
    /// priced subset would understate the spend without saying so, and an unknown cost is reported
    /// as unknown, never as zero. Per question asked: the run's candidate spend over its answer rows,
    /// failed and ungraded answers included.
    /// </summary>
    public double? CandidateCostPerQuestionUsd { get; set; }

    /// <summary>For a battery result, per battery pass: the sum over suites of the mean per run.</summary>
    public double? CandidateCostPerRunUsd { get; set; }
    public double? CandidateTotalCostUsd { get; set; }

    /// <summary>
    /// Mean cost of one run behind the entry with every role included: candidate, assessor, second
    /// opinion, claim verifier and final synthesis. Null unless every run resolved a card for every role
    /// that spent tokens, and every run recorded per-role usage (harness 15 or later). For a battery
    /// result, per battery pass.
    /// </summary>
    public double? TotalRunCostPerRunUsd { get; set; }

    /// <summary>Sample SD of the per-run total across the entry's runs. Null below two runs, when the total is null, and for a battery result.</summary>
    public double? TotalRunCostSdUsd { get; set; }

    /// <summary>Why <see cref="TotalRunCostPerRunUsd"/> is null, in one sentence. Null when it is present.</summary>
    public string? TotalRunCostUnavailableReason { get; set; }

    /// <summary>
    /// Questions each run behind the entry asked, averaged: the denominator of
    /// <see cref="CandidateCostPerQuestionUsd"/>. For a battery result, per battery pass.
    /// </summary>
    public double? QuestionsAskedPerRun { get; set; }

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
    /// <summary>`run:12`, `group:3` or `battery:7` — stable across a refresh, and what a chart keys its series by.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>`Run`, `Group` or `Battery`.</summary>
    public string SourceKind { get; set; } = string.Empty;

    public long SourceId { get; set; }

    /// <summary>The group's or the battery's name; null for a single run.</summary>
    public string? SourceName { get; set; }

    /// <summary>The runs behind the point; for a battery result, its usable member runs.</summary>
    public List<long> RunIds { get; set; } = new();

    /// <summary><i>R</i>: runs behind this point. For a battery result, every usable member run of every suite.</summary>
    public int RunCount { get; set; }

    /// <summary>Null for a battery result, which spans several suites.</summary>
    public long? SuiteId { get; set; }
    public string? SuiteName { get; set; }

    // --- A battery result only; null on a run or group entry ---
    public long? BatteryRunId { get; set; }
    public string? BatteryName { get; set; }
    public string? BatteryDefinitionSha256 { get; set; }

    /// <summary>The comparability class of the battery's latest analysis; null when it has none.</summary>
    public string? BatteryComparabilityClassSha256 { get; set; }

    public int? SuiteCount { get; set; }
    public int? RunsPerSuite { get; set; }

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
    /// <summary>`Runs` (runs and analysis groups) or `Batteries` (battery results).</summary>
    public string SubjectKind { get; set; } = BenchmarkModelComparisonSubjectKinds.Runs;

    public string PricingBasis { get; set; } = string.Empty;

    /// <summary>Ready for the chart subtitle, including the date the basis was taken.</summary>
    public string PricingBasisLabel { get; set; } = string.Empty;

    public DateTime ComputedAtUtc { get; set; }

    /// <summary>Null on a comparison of battery results.</summary>
    public long? BaselineSuiteId { get; set; }
    public string? BaselineSuiteName { get; set; }

    /// <summary>The battery the baseline condition ran; null on a comparison of runs and groups.</summary>
    public string? BaselineBatteryName { get; set; }

    /// <summary>The entries that define the baseline condition — those that may be charted.</summary>
    public List<string> BaselineEntryKeys { get; set; } = new();

    /// <summary>
    /// The baseline's value for every must-match key, so a report can print the condition. For
    /// battery results, the definition hash and the comparability class under `BatteryDefinition`
    /// and `BatteryComparabilityClass`.
    /// </summary>
    public Dictionary<string, string> BaselineKeyValues { get; set; } = new();

    /// <summary>
    /// The must-match signature of the baseline condition: the one short string that names the
    /// instrument the charted points were measured under, so an exported figure carries its own
    /// provenance. For battery results, the comparability class hash. Empty when nothing reached the
    /// baseline.
    /// </summary>
    public string BaselineSignature { get; set; } = string.Empty;

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

    /// <summary>
    /// Judge-family diagnostics over the charted entries. Null unless at least one run in the
    /// comparison is a panel run; a not-applicable result, with its reason, when the entries were not
    /// all graded by the same panel.
    /// </summary>
    public BenchmarkPanelDiagnosticsDto? PanelDiagnostics { get; set; }
}

/// <summary>The values of <see cref="BenchmarkModelComparisonDto.SubjectKind"/>.</summary>
public static class BenchmarkModelComparisonSubjectKinds
{
    public const string Runs = "Runs";
    public const string Batteries = "Batteries";
}

/// <summary>A measure the comparison deliberately refuses to chart, and the reason.</summary>
public class BenchmarkModelComparisonExcludedMeasureDto
{
    public string Measure { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// One plain-language sentence: what the catch is, for a reader who will not follow the reason.
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Where the reader should look instead.</summary>
    public string Instead { get; set; } = string.Empty;
}

// Judge-family diagnostics: mirrors of BenchmarkPanelDiagnosticsResult and its records. Member
// strings are "A" and "B". An estimate with insufficient data carries a null value and interval.

/// <summary>
/// Cross-run judge-family diagnostics for a comparison whose entries were all graded by one panel.
/// When <see cref="Applicable"/> is false the lists are empty, the labels null, and
/// <see cref="NotApplicableReason"/> says why.
/// </summary>
public class BenchmarkPanelDiagnosticsDto
{
    public bool Applicable { get; set; }
    public string? NotApplicableReason { get; set; }
    public string? MemberALabel { get; set; }
    public string? MemberAProvider { get; set; }
    public string? MemberBLabel { get; set; }
    public string? MemberBProvider { get; set; }
    public string? ReferenceLabel { get; set; }
    public string? ReferenceProvider { get; set; }
    public List<BenchmarkPanelEntryIndicesDto> Entries { get; set; } = new();

    /// <summary>Entry pairs ordered differently under member A and under member B.</summary>
    public List<BenchmarkPanelRankedPairDto> JudgeDependentPairs { get; set; } = new();

    /// <summary>Entry pairs ordered differently under the panel and under the reference reader.</summary>
    public List<BenchmarkPanelRankedPairDto> ReferenceDependentPairs { get; set; } = new();

    public List<BenchmarkPanelFamilyGapDto> FamilyGaps { get; set; } = new();
    public List<BenchmarkPanelAuditCellDto> AccusationAudit { get; set; } = new();
    public List<BenchmarkPanelMemberAuditSummaryDto> AuditSummaries { get; set; } = new();

    /// <summary>What each figure can and cannot show, in plain language.</summary>
    public List<string> Caveats { get; set; } = new();
}

/// <summary>One entry's index under each member alone, the panel and the reference reader, with its rank under each.</summary>
public class BenchmarkPanelEntryIndicesDto
{
    public string EntryKey { get; set; } = string.Empty;
    public string EntryLabel { get; set; } = string.Empty;
    public string? CandidateProvider { get; set; }
    public int? MemberAIndex { get; set; }
    public int? MemberBIndex { get; set; }
    public int? PanelIndex { get; set; }

    /// <summary>Null unless every answer of the entry has a reference score.</summary>
    public int? ReferenceIndex { get; set; }

    public int? RankA { get; set; }
    public int? RankB { get; set; }
    public int? RankPanel { get; set; }
    public int? RankReference { get; set; }
}

/// <summary>Two entries whose order depends on which reader graded them.</summary>
public class BenchmarkPanelRankedPairDto
{
    public string FirstEntryKey { get; set; } = string.Empty;
    public string FirstEntryLabel { get; set; } = string.Empty;
    public string SecondEntryKey { get; set; } = string.Empty;
    public string SecondEntryLabel { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

/// <summary>A point estimate with its 95 % interval; all null when the data are insufficient.</summary>
public class BenchmarkPanelEstimateDto
{
    public double? Value { get; set; }
    public double? CiLow { get; set; }
    public double? CiHigh { get; set; }
}

/// <summary>
/// The mean per-question quality gap between two candidate providers (Provider1 − Provider2) under
/// each reader. The interaction contrast and asymmetry estimate exist only for the pair of the two
/// members' own providers.
/// </summary>
public class BenchmarkPanelFamilyGapDto
{
    public string Provider1 { get; set; } = string.Empty;
    public string Provider2 { get; set; } = string.Empty;
    public int PairedQuestionCount { get; set; }
    public bool InsufficientData { get; set; }
    public bool IsMemberProviderPair { get; set; }
    public BenchmarkPanelEstimateDto GapA { get; set; } = new();
    public BenchmarkPanelEstimateDto GapB { get; set; } = new();
    public BenchmarkPanelEstimateDto GapPanel { get; set; } = new();
    public BenchmarkPanelEstimateDto? GapRef { get; set; }
    public BenchmarkPanelEstimateDto? InteractionContrast { get; set; }
    public string? InteractionContrastLabel { get; set; }
    public BenchmarkPanelEstimateDto? AsymmetryEstimate { get; set; }
    public string? AsymmetryEstimateLabel { get; set; }
}

/// <summary>
/// One member's charges against one candidate provider's answers, by the claim verifier's verdict.
/// Overturned means the verifier supported the accused statement.
/// </summary>
public class BenchmarkPanelAuditCellDto
{
    public string Member { get; set; } = string.Empty;
    public string MemberProvider { get; set; } = string.Empty;
    public string CandidateProvider { get; set; } = string.Empty;
    public bool SameFamily { get; set; }
    public int Charges { get; set; }
    public int Overturned { get; set; }
    public int Upheld { get; set; }
    public int Indeterminate { get; set; }
    public double? OverturnRate { get; set; }
}

/// <summary>
/// A member's overturn rate on other-family candidates minus its rate on same-family candidates.
/// Null when either side has too few charges.
/// </summary>
public class BenchmarkPanelMemberAuditSummaryDto
{
    public string Member { get; set; } = string.Empty;
    public string MemberProvider { get; set; } = string.Empty;
    public double? FamilyOverturnGap { get; set; }
}
