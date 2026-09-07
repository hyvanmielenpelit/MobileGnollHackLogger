namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using MobileGnollHackLogger.Data;

/// <summary>
/// Knobs for a group analysis. Everything here changes what is reported, never what was measured.
/// </summary>
public sealed record BenchmarkGroupStatisticsOptions
{
    /// <summary>
    /// Sample standard deviation of an item's cross-run quality scores at or above which the item
    /// is flagged unstable. An unstable item is the one where a single run's verdict is least
    /// trustworthy, which is a suite-health signal as much as a model one.
    /// </summary>
    public double UnstableItemStandardDeviation { get; init; } = BenchmarkGroupStatistics.DefaultUnstableItemStandardDeviation;

    /// <summary>Speed aggregates mix timing conditions. Set from the group's comparability tier.</summary>
    public bool SpeedDegraded { get; init; }

    /// <summary>Reason text rendered beside a degraded speed aggregate.</summary>
    public string? SpeedDegradedReason { get; init; }

    /// <summary>Cost aggregates mix pricing or timing conditions. Set from the comparability tier.</summary>
    public bool CostDegraded { get; init; }

    /// <summary>Reason text rendered beside a degraded cost aggregate.</summary>
    public string? CostDegradedReason { get; init; }

    /// <summary>
    /// Derives the degraded flags from a resolved comparability tier, so a caller cannot compute
    /// a clean-looking aggregate over a Tier B group by forgetting to set them.
    /// </summary>
    public static BenchmarkGroupStatisticsOptions FromComparability(BenchmarkComparabilityResult comparability)
    {
        ArgumentNullException.ThrowIfNull(comparability);

        string reason = comparability.Differences.Count == 0
            ? comparability.Explanation
            : string.Join("; ", comparability.Differences
                .Where(d => d.Kind == BenchmarkComparabilityKeyKind.SpeedAndCost)
                .Select(d => d.Describe()));

        return new BenchmarkGroupStatisticsOptions
        {
            SpeedDegraded = comparability.SpeedAggregatesDegraded,
            SpeedDegradedReason = comparability.SpeedAggregatesDegraded ? reason : null,
            CostDegraded = comparability.CostAggregatesDegraded,
            CostDegradedReason = comparability.CostAggregatesDegraded ? reason : null
        };
    }
}

/// <summary>
/// One run's cost, supplied by the caller. Pricing resolution belongs to the report and cost
/// layers; this class only aggregates what it is handed, which is what keeps it free of I/O.
/// </summary>
public sealed record BenchmarkGroupRunCost
{
    public long RunId { get; init; }

    /// <summary>
    /// Cost in the report's currency, keyed by role — <c>candidate</c>, <c>assessor</c>,
    /// <c>claimVerifier</c>, or whatever the caller's cost layer names them. Roles are summed
    /// across runs by key, so the keys must be stable across the members of one group.
    /// </summary>
    public IReadOnlyDictionary<string, double> CostByRole { get; init; }
        = new Dictionary<string, double>();

    public double Total => CostByRole.Values.Sum();
}

/// <summary>
/// Cross-run statistics for one suite item, over the <i>R</i> runs of a group.
///
/// Everything <see cref="BenchmarkItemAnalysis"/> already computes for an item is carried on
/// <see cref="Analysis"/> rather than recomputed here. This record adds only the figures a
/// replicate set makes meaningful and a mixed suite history does not: a sample standard deviation,
/// a median, an IQR, a coefficient of variation, a <i>t</i> interval on the item mean, and the
/// critical-error rate.
/// </summary>
public sealed record BenchmarkGroupItemStatistics
{
    public long QuestionId { get; init; }
    public int OrderIndex { get; init; }
    public string QuestionText { get; init; } = string.Empty;
    public int ItemRevision { get; init; }

    /// <summary>The difficulty this item's quality is weighted by. See <see cref="Weight"/>.</summary>
    public int? AssessedDifficulty { get; init; }

    /// <summary>
    /// The fixed weight this item carries in every per-run index computed here: the mean of the
    /// assessors' per-run difficulty ratings, falling back to the question's stored
    /// <c>AssessedDifficulty</c>, then to 50, and never below 1.
    ///
    /// Fixed across runs deliberately. A per-run weight would break the identity between the mean
    /// of the run indices and the weighted mean of the item means, and the two decompositions
    /// below only cohere because that identity holds.
    /// </summary>
    public double Weight { get; init; }

    /// <summary>Runs that produced a scored answer to this item. May be below the group's <i>R</i>.</summary>
    public int RunCount { get; init; }

    /// <summary>The item's cross-run quality scores, in run-id order.</summary>
    public IReadOnlyList<double> Scores { get; init; } = Array.Empty<double>();

    /// <summary>
    /// The runs those scores came from, positionally aligned with <see cref="Scores"/>. Carried so
    /// the per-run index reconstruction never has to re-derive which run answered which item — one
    /// definition of the sample, held once.
    /// </summary>
    public IReadOnlyList<long> RunIds { get; init; } = Array.Empty<long>();

    /// <summary>Arithmetic mean of the cross-run quality scores.</summary>
    public double Mean { get; init; }

    /// <summary>
    /// Median of the cross-run quality scores: the mean of the two central values at even
    /// <i>R</i>, the central value at odd <i>R</i>.
    /// </summary>
    public double Median { get; init; }

    public int Min { get; init; }
    public int Max { get; init; }

    /// <summary>
    /// Sample standard deviation with the <i>n</i>−1 denominator. Null below two runs, where it is
    /// undefined rather than zero.
    ///
    /// Note that this is the <b>sample</b> SD, while
    /// <see cref="BenchmarkItemStatistics.StdDev"/> on <see cref="Analysis"/> is the
    /// <b>population</b> SD over the same numbers. Both are correct for what they claim: the
    /// suite-health table describes the runs it has, and a replicate set estimates the spread of
    /// the process that produced them.
    /// </summary>
    public double? StandardDeviation { get; init; }

    /// <summary>
    /// Interquartile range: the 75th percentile minus the 25th, by linear interpolation between
    /// order statistics. Null below two runs.
    /// </summary>
    public double? InterquartileRange { get; init; }

    /// <summary>
    /// Coefficient of variation, <c>SD / mean</c>, as a fraction. Null below two runs and null at
    /// a mean of zero, where the ratio is meaningless rather than infinite.
    /// </summary>
    public double? CoefficientOfVariation { get; init; }

    /// <summary>
    /// Half-width of the 95 % confidence interval on the item mean:
    /// <c>t(R−1) · SD / √R</c>. Null below two runs.
    /// </summary>
    public double? MeanConfidenceHalfWidth { get; init; }

    /// <summary>
    /// The interval bounds, clamped to the score range [0, 100]. A quality score cannot leave that
    /// range, so an unclamped bound is not a wider claim — it is an impossible one.
    /// <see cref="MeanConfidenceHalfWidth"/> is deliberately left unclamped: it is the quantity
    /// that carries the spread, and clamping it would corrupt the quadrature in
    /// <see cref="BenchmarkGroupIndexStatistics"/>.
    /// </summary>
    public double? MeanConfidenceLower { get; init; }

    /// <inheritdoc cref="MeanConfidenceLower"/>
    public double? MeanConfidenceUpper { get; init; }

    /// <summary>
    /// Either bound hit the score range and was clamped, so the interval as reported is narrower
    /// than <see cref="MeanConfidenceHalfWidth"/> implies. Reports say so rather than presenting a
    /// truncated interval as a tight one.
    /// </summary>
    public bool MeanConfidenceTruncated { get; init; }

    /// <summary>Runs in which the assessor flagged a critical error on this item.</summary>
    public int CriticalErrorCount { get; init; }

    /// <summary>
    /// <c>k / R</c>: the share of runs in which this item drew a critical error. A rate strictly
    /// between 0 and 1 is the signal worth acting on — it means the same question sometimes does
    /// and sometimes does not trip the ceiling, which is either a genuinely borderline answer or a
    /// rubric that does not decide the case.
    /// </summary>
    public double CriticalErrorRate { get; init; }

    /// <summary>Median model-attributable time across the item's runs, in milliseconds.</summary>
    public double? MedianModelTimeMs { get; init; }

    /// <summary>
    /// <see cref="StandardDeviation"/> reached
    /// <see cref="BenchmarkGroupStatisticsOptions.UnstableItemStandardDeviation"/>.
    /// </summary>
    public bool Unstable { get; init; }

    /// <summary>
    /// Below <see cref="BenchmarkGroupStatistics.MinRunsForReproducibility"/> runs answered this
    /// item, so its spread figures are shown but are not measurements.
    /// </summary>
    public bool InsufficientRuns { get; init; }

    /// <summary>
    /// The suite-health row for this item over the same runs, from
    /// <see cref="BenchmarkItemAnalysis"/>. Carries discrimination, the difficulty delta, the
    /// budget-bound fraction and the confound flags — none of which are recomputed here.
    /// </summary>
    public BenchmarkItemStatistics? Analysis { get; init; }
}

/// <summary>
/// The pooled Intelligence Index of a group and its two independent uncertainty components.
///
/// The components are reported separately and only then combined, because they answer different
/// questions and behave differently as more runs are added. Collapsing them into one number would
/// hide the fact that only one of them can be bought down with more runs.
/// </summary>
public sealed record BenchmarkGroupIndexStatistics
{
    /// <summary><i>R</i>: runs that contributed an index.</summary>
    public int RunCount { get; init; }

    /// <summary>
    /// The multi-run Intelligence Index: the mean of the <i>R</i> per-run difficulty-weighted
    /// indices, recomputed here from the group's fixed item weights rather than read off the
    /// stored, integer-rounded <c>BenchmarkRun.QualityIndex</c>.
    /// </summary>
    public double PointEstimate { get; init; }

    /// <summary>
    /// The same quantity by the other route: the difficulty-weighted mean of the per-item
    /// cross-run means. For a fixed item set answered by every run these are identically equal,
    /// which is what makes the variance decomposition below coherent.
    /// </summary>
    public double WeightedMeanOfItemMeans { get; init; }

    /// <summary>
    /// <see cref="PointEstimate"/> and <see cref="WeightedMeanOfItemMeans"/> agree to within
    /// floating-point tolerance. False only when the item set is ragged — some run failed to
    /// produce a scored answer to some item — in which case the two routes weight the missing
    /// cells differently and the report must say which number it is showing.
    /// </summary>
    public bool IdentityHolds { get; init; }

    /// <summary>The per-run indices, in run-id order.</summary>
    public IReadOnlyList<double> PerRunIndices { get; init; } = Array.Empty<double>();

    /// <summary>
    /// Mean of the stored <c>BenchmarkRun.QualityIndex</c> values, for cross-checking only. It
    /// differs from <see cref="PointEstimate"/> by rounding and by per-run difficulty weights, and
    /// nothing is computed from it.
    /// </summary>
    public double? MeanStoredQualityIndex { get; init; }

    /// <summary>
    /// Sample standard deviation of the <i>R</i> per-run indices. Null below
    /// <see cref="BenchmarkGroupStatistics.MinRunsForReproducibility"/> runs, mirroring the
    /// <c>n &lt; 3 → null</c> convention in
    /// <see cref="BenchmarkScoring.QualityIndexStandardError(IEnumerable{ValueTuple{int?, int?}})"/>
    /// and <see cref="BenchmarkItemAnalysis.MinRunsForMeasurement"/>.
    /// </summary>
    public double? ReproducibilityStandardDeviation { get; init; }

    /// <summary>
    /// <c>SD(run indices) / √R</c>. Answers <i>"would a re-run move this?"</i> and
    /// <b>shrinks with R</b>. Null below three runs.
    /// </summary>
    public double? ReproducibilityStandardError { get; init; }

    /// <summary>The <i>t</i>(<i>R</i>−1) two-sided 95 % critical value. Null below three runs.</summary>
    public double? ReproducibilityCriticalValue { get; init; }

    /// <summary><c>t(R−1) · SE_repro</c>. Null below three runs.</summary>
    public double? ReproducibilityHalfWidth { get; init; }

    /// <summary>
    /// <see cref="BenchmarkScoring.QualityIndexStandardError(IEnumerable{ValueTuple{int?, int?}})"/>
    /// applied to the per-item <b>cross-run mean</b> qualities. Answers <i>"would a different 18
    /// questions move this?"</i>
    ///
    /// <b>It does not shrink with R</b>, because every run answers the same items: adding runs
    /// sharpens the estimate of each item's mean, and does nothing at all about the fact that the
    /// suite drew those particular items. A reader who expects it to fall as √<i>R</i> will
    /// conclude the code is broken, so the report states this explicitly.
    ///
    /// Null below three items, from the reused method's own convention.
    /// </summary>
    public double? ItemSamplingStandardError { get; init; }

    /// <summary>The normal 95 % critical value, 1.96, used for the item-sampling component.</summary>
    public double ItemSamplingCriticalValue { get; init; } = BenchmarkGroupStatistics.NormalCritical95;

    /// <summary><c>1.96 · SE_item</c>. Null when the item-sampling SE is null.</summary>
    public double? ItemSamplingHalfWidth { get; init; }

    /// <summary>
    /// <c>√((t · SE_repro)² + (1.96 · SE_item)²)</c> — the two components combined in quadrature,
    /// because item selection and run-to-run variation are independent sources.
    ///
    /// When the reproducibility component is unavailable (<i>R</i> &lt; 3) this is the
    /// item-sampling half-width alone, and <see cref="ReproducibilityAvailable"/> is false so the
    /// report can say the interval covers one source rather than two.
    /// </summary>
    public double? CombinedHalfWidth { get; init; }

    /// <summary>
    /// The combined interval's bounds, clamped to the score range [0, 100] for the same reason the
    /// per-item bounds are. <see cref="CombinedHalfWidth"/> stays unclamped.
    /// </summary>
    public double? CombinedLower { get; init; }

    /// <inheritdoc cref="CombinedLower"/>
    public double? CombinedUpper { get; init; }

    /// <summary>Either combined bound hit the score range and was clamped.</summary>
    public bool CombinedIntervalTruncated { get; init; }

    /// <summary>False below three runs, where no reproducibility figure is reported at all.</summary>
    public bool ReproducibilityAvailable { get; init; }
}

/// <summary>Speed across the runs of a group.</summary>
public sealed record BenchmarkGroupSpeedStatistics
{
    public int RunCount { get; init; }

    /// <summary>Mean of the stored per-run Speed Indices.</summary>
    public double? MeanSpeedIndex { get; init; }

    /// <summary>Sample standard deviation of the per-run Speed Indices. Null below two runs.</summary>
    public double? SpeedIndexStandardDeviation { get; init; }

    public IReadOnlyList<double> PerRunSpeedIndices { get; init; } = Array.Empty<double>();

    /// <summary>Answers pooled across all <i>R</i> × <i>Q</i> cells.</summary>
    public int PooledAnswerCount { get; init; }

    /// <summary>
    /// Percentiles of model-attributable time over every answer of every run, by linear
    /// interpolation between order statistics. Pooled rather than averaged per run: the question
    /// is what a single slow turn looks like, and a mean of per-run medians cannot answer it.
    /// </summary>
    public double? ModelTimeP50Ms { get; init; }

    public double? ModelTimeP90Ms { get; init; }
    public double? ModelTimeMaxMs { get; init; }

    /// <summary>Speed aggregates mix timing conditions. See the group's comparability tier.</summary>
    public bool Degraded { get; init; }

    public string? DegradedReason { get; init; }

    /// <summary>
    /// The standing speed caveat: the index is comparable only within one thinking level and one
    /// timing mode, which the Tier A comparability keys enforce.
    /// </summary>
    public string Caveat { get; init; } = BenchmarkGroupStatistics.SpeedCaveat;
}

/// <summary>Cost across the runs of a group, aggregated from caller-supplied per-run costs.</summary>
public sealed record BenchmarkGroupCostStatistics
{
    public int RunCount { get; init; }

    public double TotalCost { get; init; }

    public double MeanCostPerRun { get; init; }

    /// <summary>Sample standard deviation of the per-run totals. Null below two runs.</summary>
    public double? CostStandardDeviation { get; init; }

    public IReadOnlyDictionary<string, double> TotalCostByRole { get; init; }
        = new Dictionary<string, double>();

    public IReadOnlyDictionary<string, double> MeanCostByRole { get; init; }
        = new Dictionary<string, double>();

    /// <summary>
    /// The per-run totals, in member order. Cost is the least reproducible quantity a replicate set
    /// measures — a set whose 22 comparability keys match can still spend twice as much on one
    /// member as another — so the individual figures are carried rather than only their mean.
    /// </summary>
    public IReadOnlyList<double> PerRunTotals { get; init; } = Array.Empty<double>();

    /// <summary>
    /// Sample standard deviation of each role's per-run cost. Null per role below two runs.
    ///
    /// <para>Without this the reader sees a spread on the total and cannot tell which role carries
    /// it. A run absent from one member's cost dictionary contributes <c>0.0</c> rather than being
    /// skipped: a run that spent nothing on a role did spend nothing.</para>
    /// </summary>
    public IReadOnlyDictionary<string, double?> CostStandardDeviationByRole { get; init; }
        = new Dictionary<string, double?>();

    public IReadOnlyDictionary<string, double> MinCostByRole { get; init; }
        = new Dictionary<string, double>();

    public IReadOnlyDictionary<string, double> MaxCostByRole { get; init; }
        = new Dictionary<string, double>();

    /// <summary>Mean cost per run divided by the number of items. Null when there are no items.</summary>
    public double? CostPerQuestion { get; init; }

    /// <summary>
    /// Mean cost per run divided by the multi-run Intelligence Index. Null at a non-positive
    /// index, where the ratio would invert its own meaning.
    /// </summary>
    public double? CostPerIndexPoint { get; init; }

    /// <summary>
    /// The pricing snapshot or the timing mode differs across members, so these figures mix
    /// conditions. A cost mismatch degrades <b>cost only</b> — prices cannot move a quality score.
    /// </summary>
    public bool Degraded { get; init; }

    public string? DegradedReason { get; init; }
}

/// <summary>
/// One scoring dimension across the runs of a group.
///
/// <para>These means are <b>unweighted</b>, unlike the Intelligence Index, which weights each item
/// by its assessed difficulty. There are no per-dimension difficulty weights to apply, so a reader
/// comparing a dimension mean against the index must be able to see that the two are differently
/// weighted by design rather than inconsistent.</para>
/// </summary>
public sealed record BenchmarkGroupDimensionStatistics
{
    /// <summary><c>Accuracy</c>, <c>Completeness</c>, <c>Conciseness</c> or <c>Readability</c>.</summary>
    public string Dimension { get; init; } = string.Empty;

    /// <summary>
    /// One unweighted mean per member, over that member's scored answers. Empty when no member
    /// recorded this dimension — emitted as an empty list rather than omitting the dimension, so a
    /// consumer never has to distinguish "absent" from "unscored".
    /// </summary>
    public IReadOnlyList<double> PerRunMeans { get; init; } = Array.Empty<double>();

    /// <summary>Mean of <see cref="PerRunMeans"/>. Null when nothing was scored.</summary>
    public double? Mean { get; init; }

    /// <summary>Sample standard deviation of <see cref="PerRunMeans"/>. Null below two runs.</summary>
    public double? StandardDeviation { get; init; }

    /// <summary>
    /// <c>t(R−1) · SD / √R</c>. Null below
    /// <see cref="BenchmarkGroupStatistics.MinRunsForReproducibility"/> runs, matching the
    /// reproducibility rule on the index: below three runs a standard deviation over runs is
    /// arithmetic rather than evidence.
    /// </summary>
    public double? ConfidenceHalfWidth { get; init; }

    public double? Min { get; init; }

    public double? Max { get; init; }

    /// <summary>
    /// Cross-run mean of this dimension per item, keyed by question id. Carried so a consumer can
    /// name the weakest items on a dimension without a second pass over every answer.
    /// </summary>
    public IReadOnlyDictionary<long, double> ItemMeans { get; init; }
        = new Dictionary<long, double>();
}

/// <summary>
/// Token, tool and claim-verification totals pooled across the runs of a group.
///
/// <para>The three role token totals are kept apart, exactly as the run row keeps them: the
/// candidate's consumption measures the model under test and must never absorb a grader's. On a
/// set where the claim verifier is most of the spend, that separation is the whole finding.</para>
/// </summary>
public sealed record BenchmarkGroupUsageStatistics
{
    public int RunCount { get; init; }

    // --- Candidate ---
    public long TotalInputTokens { get; init; }
    public long TotalOutputTokens { get; init; }
    public long TotalCacheReadTokens { get; init; }

    /// <summary>Cache reads as a percentage of input tokens. Null at zero input.</summary>
    public double? CacheReadSharePercentage { get; init; }

    /// <summary>
    /// Input divided by output, taken from the sums rather than averaged over runs: the question is
    /// what the set consumed, and a mean of per-run ratios answers a different one. Null at zero
    /// output.
    /// </summary>
    public double? InputOutputRatio { get; init; }

    public IReadOnlyList<long> PerRunInputTokens { get; init; } = Array.Empty<long>();

    /// <summary>Sample standard deviation of the per-run candidate input totals. Null below two runs.</summary>
    public double? InputTokenStandardDeviation { get; init; }

    // --- Graders, never folded into the candidate totals above ---
    public long TotalAssessmentInputTokens { get; init; }
    public long TotalAssessmentOutputTokens { get; init; }
    public long TotalClaimVerificationInputTokens { get; init; }
    public long TotalClaimVerificationOutputTokens { get; init; }

    // --- Tool routing, pooled over every member's answers ---
    public int TotalToolCalls { get; init; }

    public double? MeanToolCallsPerRun { get; init; }

    /// <summary>Sample standard deviation of the per-run tool-call counts. Null below two runs.</summary>
    public double? ToolCallStandardDeviation { get; init; }

    public IReadOnlyDictionary<string, int> ToolCallsByFamily { get; init; }
        = new Dictionary<string, int>();

    /// <summary>Each family's share of <see cref="TotalToolCalls"/>, as a percentage.</summary>
    public IReadOnlyDictionary<string, double> ToolFamilyShares { get; init; }
        = new Dictionary<string, double>();

    // --- Claim verification: what the verifier's spend bought ---
    public int ClaimsSupported { get; init; }
    public int ClaimsRefuted { get; init; }
    public int ClaimsIndeterminate { get; init; }

    /// <summary>The three counts above, summed.</summary>
    public int ClaimsChecked { get; init; }

    /// <summary>Answers for which a claim verification was recorded, pooled across members.</summary>
    public int AnswersWithVerification { get; init; }
}

/// <summary>
/// The candidate prompt configuration the group was graded under, decoded from the first member.
///
/// <para>Reading one member is sound because the group's comparability key covers
/// <c>CandidatePromptOptions</c>: a poolable group cannot disagree on it.
/// <see cref="Divergent"/> asserts that rather than assuming it.</para>
///
/// <para>This exists because attributing any dimensional result to a model requires knowing what
/// the model was told to do — a concise instruction caps Completeness by design — and a report that
/// omits the configuration cannot support that check.</para>
/// </summary>
public sealed record BenchmarkGroupPromptUnderTest
{
    /// <summary>False when the first member recorded no prompt options. Nothing below is meaningful.</summary>
    public bool Recorded { get; init; }

    /// <summary>Any member's prompt signature differs from the first member's.</summary>
    public bool Divergent { get; init; }

    public int OverseerMode { get; init; }
    public bool VerboseMode { get; init; }
    public bool SpoilerFreeMode { get; init; }
    public bool EnableToolUse { get; init; }
    public bool EnableWebSearch { get; init; }
    public bool EnableSubAgents { get; init; }
    public bool AllowSourceCodeReferences { get; init; }
    public bool IsGameOn { get; init; }
    public bool DeveloperMode { get; init; }
    public bool HasMessageHistory { get; init; }
    public bool HasWikiContext { get; init; }
    public bool HasGameSnapshot { get; init; }

    /// <summary>
    /// The tool batching mode, which selects a policy file and is therefore part of the prompt
    /// text rather than a setting beside it.
    /// </summary>
    /// <remarks>Qualified: <c>System.Linq</c> declares a type of the same name.</remarks>
    public MobileGnollHackLogger.Data.ParallelExecutionMode ParallelMode { get; init; }
}

/// <summary>The whole group analysis. Pure arithmetic; every figure is reproducible.</summary>
public sealed record BenchmarkGroupStatisticsResult
{
    public long SuiteId { get; init; }
    public string SuiteName { get; init; } = string.Empty;

    /// <summary>The member runs, in ascending id order.</summary>
    public IReadOnlyList<long> RunIds { get; init; } = Array.Empty<long>();

    /// <summary><i>R</i>.</summary>
    public int RunCount { get; init; }

    /// <summary><i>Q</i>: items with at least one scored answer across the group.</summary>
    public int ItemCount { get; init; }

    /// <summary>Items the group's questions contain that no member answered.</summary>
    public int UnansweredItemCount { get; init; }

    public IReadOnlyList<BenchmarkGroupItemStatistics> Items { get; init; }
        = Array.Empty<BenchmarkGroupItemStatistics>();

    public BenchmarkGroupIndexStatistics Index { get; init; } = new();

    public BenchmarkGroupSpeedStatistics Speed { get; init; } = new();

    /// <summary>Null when the caller supplied no costs.</summary>
    public BenchmarkGroupCostStatistics? Cost { get; init; }

    /// <summary>
    /// The four scoring dimensions, always in Accuracy, Completeness, Conciseness, Readability
    /// order. A dimension no member scored is present with empty <c>PerRunMeans</c>.
    /// </summary>
    public IReadOnlyList<BenchmarkGroupDimensionStatistics> Dimensions { get; init; }
        = Array.Empty<BenchmarkGroupDimensionStatistics>();

    /// <summary>Null when no member recorded any token or tool usage.</summary>
    public BenchmarkGroupUsageStatistics? Usage { get; init; }

    /// <summary>The prompt configuration the members were graded under. Null when there are none.</summary>
    public BenchmarkGroupPromptUnderTest? PromptUnderTest { get; init; }

    /// <summary>The suite-health analysis over exactly this group's members.</summary>
    public BenchmarkSuiteItemAnalysis ItemAnalysis { get; init; } = new();

    public IReadOnlyList<long> UnstableQuestionIds { get; init; } = Array.Empty<long>();

    /// <summary>
    /// <i>R</i> reached <see cref="BenchmarkGroupStatistics.MinRunsForReproducibility"/>, so the
    /// pooled index may be reported with both interval components.
    /// </summary>
    public bool PooledIndexReportable { get; init; }

    /// <summary>
    /// What multi-run cannot decompose. Carried on the result rather than left to the report
    /// builder so that every surface rendering these numbers has the sentence available.
    /// </summary>
    public string VarianceDecompositionCaveat { get; init; }
        = BenchmarkGroupStatistics.VarianceDecompositionCaveat;
}

/// <summary>The Wilcoxon signed-rank test over a vector of paired differences.</summary>
public sealed record BenchmarkWilcoxonSignedRankResult
{
    /// <summary>Pairs entering the test, after zero differences are discarded.</summary>
    public int SampleSize { get; init; }

    /// <summary>
    /// Pairs whose difference was exactly zero. Discarded before ranking — Wilcoxon's own
    /// reduction — and reported, because discarding them lowers the effective sample size and a
    /// reader is entitled to know by how much.
    /// </summary>
    public int ZeroDifferenceCount { get; init; }

    /// <summary>Sum of the ranks of the positive differences.</summary>
    public double PositiveRankSum { get; init; }

    /// <summary>Sum of the ranks of the negative differences.</summary>
    public double NegativeRankSum { get; init; }

    /// <summary><c>min(W+, W−)</c>, the conventional two-sided statistic.</summary>
    public double Statistic { get; init; }

    /// <summary>Two-sided p-value. Null when the test could not be run at all.</summary>
    public double? PValue { get; init; }

    /// <summary>
    /// Which null distribution produced <see cref="PValue"/>: <c>exact</c> or
    /// <c>normal approximation</c>. See <see cref="BenchmarkGroupStatistics.MaxExactWilcoxonSampleSize"/>.
    /// </summary>
    public string Method { get; init; } = "not computed";

    /// <summary>Ties among the absolute differences produced fractional average ranks.</summary>
    public bool TiesPresent { get; init; }
}

/// <summary>A paired <i>t</i>-test, reported beside Wilcoxon and never instead of it.</summary>
public sealed record BenchmarkPairedTTestResult
{
    public int SampleSize { get; init; }
    public double MeanDifference { get; init; }
    public double? StandardError { get; init; }
    public double? TStatistic { get; init; }
    public double? DegreesOfFreedom { get; init; }
    public double? PValue { get; init; }
}

/// <summary>
/// One item's difference between two groups. <b>Exploratory.</b> Eighteen simultaneous item tests
/// without correction would manufacture findings, so these carry Benjamini–Hochberg adjusted
/// p-values and must be labelled exploratory wherever they are rendered.
/// </summary>
public sealed record BenchmarkGroupItemComparison
{
    public long QuestionId { get; init; }
    public int OrderIndex { get; init; }
    public string QuestionText { get; init; } = string.Empty;

    public int BaselineRunCount { get; init; }
    public int TreatmentRunCount { get; init; }

    public double BaselineMean { get; init; }
    public double TreatmentMean { get; init; }

    /// <summary>Treatment minus baseline. Positive means the treatment scored higher.</summary>
    public double Difference { get; init; }

    /// <summary>Welch's <i>t</i> over the two groups' per-run scores for this item.</summary>
    public double? TStatistic { get; init; }

    public double? DegreesOfFreedom { get; init; }

    /// <summary>Unadjusted two-sided p-value. Null when either side had fewer than two runs.</summary>
    public double? PValue { get; init; }

    /// <summary>Benjamini–Hochberg adjusted p-value (a q-value), monotone in the raw p.</summary>
    public double? AdjustedPValue { get; init; }

    /// <summary>Rejected at the group comparison's false discovery rate.</summary>
    public bool RejectedAtFdr { get; init; }

    /// <summary>Always true. Present so a UI cannot render one of these without the label.</summary>
    public bool Exploratory { get; init; } = true;
}

/// <summary>One p-value's place in a Benjamini–Hochberg procedure.</summary>
public sealed record BenchmarkFdrResult
{
    /// <summary>Position in the input list, so the caller can join back without re-sorting.</summary>
    public int Index { get; init; }

    public double PValue { get; init; }

    /// <summary>The step-up adjusted p-value (q-value), clamped to 1 and monotone in the raw p.</summary>
    public double AdjustedPValue { get; init; }

    public bool Rejected { get; init; }
}

/// <summary>
/// A paired comparison of two groups, by question, on per-item cross-run mean quality. This is the
/// T15 use case: a baseline replicate set and a treatment replicate set that differ on exactly one
/// instrument key.
/// </summary>
public sealed record BenchmarkGroupComparison
{
    public IReadOnlyList<long> BaselineRunIds { get; init; } = Array.Empty<long>();
    public IReadOnlyList<long> TreatmentRunIds { get; init; } = Array.Empty<long>();

    /// <summary>Items answered by at least one run on <b>both</b> sides. Only these are paired.</summary>
    public int PairedItemCount { get; init; }

    /// <summary>Items present in one group and not the other, and therefore excluded.</summary>
    public int UnpairedItemCount { get; init; }

    /// <summary>Mean of the per-item differences, treatment minus baseline.</summary>
    public double MeanDifference { get; init; }

    /// <summary>Sample standard deviation of the per-item differences. Null below two pairs.</summary>
    public double? DifferenceStandardDeviation { get; init; }

    /// <summary><c>t(n−1) · SD / √n</c> around <see cref="MeanDifference"/>.</summary>
    public double? DifferenceConfidenceHalfWidth { get; init; }

    public double? DifferenceConfidenceLower { get; init; }
    public double? DifferenceConfidenceUpper { get; init; }

    /// <summary>
    /// The primary test. Non-parametric, which is what a bounded 0–100 scale over eighteen items
    /// warrants; the paired <i>t</i> beside it assumes a normality nobody has checked.
    /// </summary>
    public BenchmarkWilcoxonSignedRankResult Wilcoxon { get; init; } = new();

    /// <summary>Secondary, reported for readers who expect it.</summary>
    public BenchmarkPairedTTestResult PairedT { get; init; } = new();

    /// <summary>
    /// Cohen's <i>d</i><sub>z</sub>: the mean paired difference divided by the standard deviation
    /// of those differences. The effect size for a paired design — not <i>d</i>, which would use a
    /// pooled between-group SD and describe a comparison nobody made here.
    /// </summary>
    public double? CohensDz { get; init; }

    /// <summary>Per-item differences. Exploratory; see <see cref="BenchmarkGroupItemComparison"/>.</summary>
    public IReadOnlyList<BenchmarkGroupItemComparison> ItemComparisons { get; init; }
        = Array.Empty<BenchmarkGroupItemComparison>();

    /// <summary>The false discovery rate the per-item rejections were controlled at.</summary>
    public double FalseDiscoveryRate { get; init; }

    public string ExploratoryNote { get; init; } = BenchmarkGroupStatistics.ExploratoryItemTestNote;

    public string VarianceDecompositionCaveat { get; init; }
        = BenchmarkGroupStatistics.VarianceDecompositionCaveat;
}

/// <summary>
/// Statistics over a multi-run replicate set. Pure functions: no I/O, no database access, no AI
/// calls, no writes.
///
/// This is the file the scientific claims of the multi-run feature rest on, so every definition it
/// implements is written out in these docs and again in
/// <c>docs/overseer/ai-benchmark-multi-run.md</c>. Two rules govern the whole file:
///
/// <list type="bullet">
/// <item><b>Per-item statistics that <see cref="BenchmarkItemAnalysis"/> already computes are
/// reused, never reimplemented.</b> The sample predicate itself comes from
/// <see cref="BenchmarkItemAnalysis.Samples"/>, so a group's denominators and the suite-health
/// panel's denominators cannot drift apart.</item>
/// <item><b>The two uncertainty components are reported separately before they are combined.</b>
/// One shrinks with more runs and one does not, and a single interval hides which.</item>
/// </list>
/// </summary>
public static class BenchmarkGroupStatistics
{
    /// <summary>
    /// Minimum <i>R</i> for a reproducibility figure. Matches
    /// <c>BenchmarkScoring.QualityIndexStandardError</c>'s <c>n &lt; 3 → null</c> rule; below it a
    /// standard deviation over runs is arithmetic rather than evidence.
    /// </summary>
    public const int MinRunsForReproducibility = 3;

    /// <summary>Sample SD at or above which an item is flagged unstable.</summary>
    public const double DefaultUnstableItemStandardDeviation = 15.0;

    /// <summary>The two-sided 95 % normal critical value.</summary>
    public const double NormalCritical95 = 1.959963984540054;

    /// <summary>The bounds a quality score, and therefore any interval reported on one, lives in.</summary>
    public const double MinScore = 0.0;

    /// <inheritdoc cref="MinScore"/>
    public const double MaxScore = 100.0;

    /// <summary>
    /// At or below this many non-zero pairs the Wilcoxon p-value is exact — the full conditional
    /// permutation distribution over the observed ranks, enumerated by dynamic programming over
    /// rank sums. Above it, the tie-corrected normal approximation with a continuity correction is
    /// used. 25 is chosen because the DP is trivial there and every realistic suite is smaller.
    /// </summary>
    public const int MaxExactWilcoxonSampleSize = 25;

    /// <summary>Default false discovery rate for the exploratory per-item tests.</summary>
    public const double DefaultFalseDiscoveryRate = 0.05;

    /// <summary>
    /// The part of the speed caveat that is true of every group. What a <i>particular</i> group's
    /// timing conditions did is appended by <see cref="BuildSpeedCaveat"/> from its own degraded
    /// flags — never asserted from its tier, which does not imply it.
    /// </summary>
    public const string SpeedCaveat =
        "Speed figures are comparable only within one thinking level and one timing mode. The "
        + "comparability keys covering both are what enforce that.";

    /// <summary>
    /// Appended to <see cref="SpeedCaveat"/> when a group's speed aggregates are degraded, naming
    /// the keys that moved.
    /// </summary>
    public const string SpeedDegradedCaveatPrefix =
        " These figures mix timing conditions: ";

    /// <summary>
    /// Appended when nothing that affects timing differs. Stated positively on purpose — silence
    /// reads as "not checked", and the check is the reassurance.
    /// </summary>
    public const string SpeedNotDegradedCaveat =
        " No comparability key affecting timing differs across these runs, so the figures are "
        + "measured under one condition.";

    public const string ExploratoryItemTestNote =
        "Per-item differences are exploratory. Each item is a separate test, so the set of them is "
        + "controlled at the stated false discovery rate by the Benjamini-Hochberg procedure; an "
        + "uncorrected pass over eighteen items would manufacture roughly one finding per comparison "
        + "by chance alone. Treat a rejection as a place to look, never as a result.";

    /// <summary>
    /// What multi-run cannot decompose, and must therefore be stated wherever its numbers appear.
    /// </summary>
    public const string VarianceDecompositionCaveat =
        "Run-to-run variance mixes candidate stochasticity with grader stochasticity: each run produces "
        + "a new answer that is graded once, so an item whose scores move across runs may have a "
        + "variable model, a variable grader, or both, and this analysis cannot tell them apart. "
        + "Separating them requires re-grading identical answers - SecondOpinionMode = All, or a "
        + "re-assessment pass over one run's answers. Until that is done, do not attribute item "
        + "instability to the model.";

    // --- Group statistics ---------------------------------------------------------------------

    /// <summary>
    /// Computes the whole analysis for one group.
    ///
    /// <paramref name="runs"/> must have their <c>Answers</c> loaded, and should be exactly the
    /// group's members — the caller resolves the comparability tier first and passes the degraded
    /// flags in through <paramref name="options"/>, so that a Tier B group cannot produce a
    /// clean-looking speed or cost aggregate.
    /// </summary>
    public static BenchmarkGroupStatisticsResult Compute(
        BenchmarkSuite suite,
        IReadOnlyCollection<BenchmarkQuestion> questions,
        IReadOnlyCollection<BenchmarkRun> runs,
        IReadOnlyCollection<BenchmarkGroupRunCost>? costs = null,
        BenchmarkGroupStatisticsOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(suite);
        questions ??= Array.Empty<BenchmarkQuestion>();
        runs ??= Array.Empty<BenchmarkRun>();
        var cfg = options ?? new BenchmarkGroupStatisticsOptions();

        var members = runs.Where(r => r != null).OrderBy(r => r.Id).ToList();
        var runIds = members.Select(r => r.Id).ToList();

        // Reuse rather than reimplement: the suite-health table over exactly these members.
        var itemAnalysis = BenchmarkItemAnalysis.Compute(suite, questions, members, runIds);
        var analysisByQuestion = itemAnalysis.Items.ToDictionary(i => i.QuestionId);

        var items = new List<BenchmarkGroupItemStatistics>();
        int unanswered = 0;

        foreach (var question in questions.OrderBy(q => q.OrderIndex))
        {
            var samples = BenchmarkItemAnalysis.Samples(question, members)
                .OrderBy(s => s.Run.Id)
                .ToList();

            if (samples.Count == 0)
            {
                unanswered++;
                continue;
            }

            items.Add(ComputeItem(question, samples, analysisByQuestion.GetValueOrDefault(question.Id), cfg));
        }

        var index = ComputeIndex(items, members);
        var speed = ComputeSpeed(members, items, cfg);
        var cost = ComputeCost(costs, items.Count, index.PointEstimate, cfg);
        var dimensions = ComputeDimensions(members, items);
        var usage = ComputeUsage(members, items);
        var promptUnderTest = ComputePromptUnderTest(members);

        return new BenchmarkGroupStatisticsResult
        {
            SuiteId = suite.Id,
            SuiteName = suite.Name,
            RunIds = runIds,
            RunCount = members.Count,
            ItemCount = items.Count,
            UnansweredItemCount = unanswered,
            Items = items,
            Index = index,
            Speed = speed,
            Cost = cost,
            Dimensions = dimensions,
            Usage = usage,
            PromptUnderTest = promptUnderTest,
            ItemAnalysis = itemAnalysis,
            UnstableQuestionIds = items.Where(i => i.Unstable).Select(i => i.QuestionId).ToList(),
            PooledIndexReportable = members.Count >= MinRunsForReproducibility
        };
    }

    private static BenchmarkGroupItemStatistics ComputeItem(
        BenchmarkQuestion question,
        IReadOnlyList<(BenchmarkRun Run, BenchmarkRunAnswer Answer)> samples,
        BenchmarkItemStatistics? analysis,
        BenchmarkGroupStatisticsOptions cfg)
    {
        var scores = samples.Select(s => (double)s.Answer.QualityScore!.Value).ToList();
        int runCount = scores.Count;

        double mean = scores.Average();
        double? sd = SampleStandardDeviation(scores);
        double? iqr = runCount >= 2
            ? Percentile(scores, 75.0)!.Value - Percentile(scores, 25.0)!.Value
            : null;
        double? cv = sd.HasValue && Math.Abs(mean) > double.Epsilon ? sd.Value / mean : null;

        double? half = null;
        if (sd.HasValue && runCount >= 2)
        {
            half = StudentTCritical95(runCount - 1) * sd.Value / Math.Sqrt(runCount);
        }

        int criticalErrors = samples.Count(s => s.Answer.CriticalError);

        // The item's weight is fixed across the group. See BenchmarkGroupItemStatistics.Weight.
        var ratings = samples
            .Where(s => s.Answer.AssessedDifficulty.HasValue)
            .Select(s => (double)s.Answer.AssessedDifficulty!.Value)
            .ToList();
        double weight = ratings.Count > 0
            ? ratings.Average()
            : question.AssessedDifficulty ?? 50.0;
        weight = Math.Max(1.0, weight);

        var modelTimes = samples.Select(s => (double)s.Answer.ModelTimeMs).ToList();

        return new BenchmarkGroupItemStatistics
        {
            QuestionId = question.Id,
            OrderIndex = question.OrderIndex,
            QuestionText = question.QuestionText,
            ItemRevision = question.ItemRevision,
            AssessedDifficulty = question.AssessedDifficulty,
            Weight = weight,
            RunCount = runCount,
            Scores = scores,
            RunIds = samples.Select(s => s.Run.Id).ToList(),
            Mean = mean,
            Median = Median(scores)!.Value,
            Min = (int)scores.Min(),
            Max = (int)scores.Max(),
            StandardDeviation = sd,
            InterquartileRange = iqr,
            CoefficientOfVariation = cv,
            MeanConfidenceHalfWidth = half,
            MeanConfidenceLower = half.HasValue ? ClampScore(mean - half.Value) : null,
            MeanConfidenceUpper = half.HasValue ? ClampScore(mean + half.Value) : null,
            MeanConfidenceTruncated = half.HasValue && ScoreBoundExceeded(mean, half.Value),
            CriticalErrorCount = criticalErrors,
            CriticalErrorRate = criticalErrors / (double)runCount,
            MedianModelTimeMs = Median(modelTimes),
            Unstable = sd.HasValue && sd.Value >= cfg.UnstableItemStandardDeviation,
            InsufficientRuns = runCount < MinRunsForReproducibility,
            Analysis = analysis
        };
    }

    /// <summary>
    /// The pooled index and its two components.
    ///
    /// The per-run indices are recomputed here from the group's fixed item weights rather than
    /// read off <c>BenchmarkRun.QualityIndex</c>. Two reasons, both load-bearing: the stored value
    /// is rounded to an integer, and it is weighted by that run's own per-answer difficulty
    /// ratings, which the assessor re-derives every run. Either alone would break the identity
    /// between the mean of the run indices and the weighted mean of the item means.
    /// </summary>
    private static BenchmarkGroupIndexStatistics ComputeIndex(
        IReadOnlyList<BenchmarkGroupItemStatistics> items,
        IReadOnlyList<BenchmarkRun> members)
    {
        if (items.Count == 0 || members.Count == 0)
        {
            return new BenchmarkGroupIndexStatistics { RunCount = members.Count };
        }

        // Scores carry the run that produced them, so no predicate is re-derived here.
        var scoreByRunAndItem = new Dictionary<long, Dictionary<long, double>>();
        foreach (var item in items)
        {
            for (int i = 0; i < item.Scores.Count && i < item.RunIds.Count; i++)
            {
                long runId = item.RunIds[i];
                if (!scoreByRunAndItem.TryGetValue(runId, out var map))
                {
                    map = new Dictionary<long, double>();
                    scoreByRunAndItem[runId] = map;
                }

                map[item.QuestionId] = item.Scores[i];
            }
        }

        var perRunIndices = new List<double>();
        foreach (var run in members)
        {
            if (!scoreByRunAndItem.TryGetValue(run.Id, out var map) || map.Count == 0) continue;

            double weighted = 0.0;
            double weights = 0.0;
            foreach (var item in items)
            {
                if (!map.TryGetValue(item.QuestionId, out double score)) continue;
                weighted += item.Weight * score;
                weights += item.Weight;
            }

            if (weights > 0.0)
            {
                perRunIndices.Add(weighted / weights);
            }
        }

        double point = perRunIndices.Count > 0 ? perRunIndices.Average() : 0.0;

        double totalWeight = items.Sum(i => i.Weight);
        double weightedItemMean = totalWeight > 0.0
            ? items.Sum(i => i.Weight * i.Mean) / totalWeight
            : 0.0;

        int r = perRunIndices.Count;
        double? reproSd = r >= MinRunsForReproducibility ? SampleStandardDeviation(perRunIndices) : null;
        double? reproSe = reproSd.HasValue ? reproSd.Value / Math.Sqrt(r) : null;
        double? tCrit = reproSe.HasValue ? StudentTCritical95(r - 1) : null;
        double? reproHalf = reproSe.HasValue && tCrit.HasValue ? tCrit.Value * reproSe.Value : null;

        // Reuse, not reimplement: the existing item-sampling standard error, applied to the
        // per-item cross-run means. The method takes integer scores and difficulties, so the means
        // and the weights are rounded on the way in; replicating a run identically leaves both
        // unchanged, which is what makes this component invariant in R.
        var itemInputs = items
            .Select(i => ((int?)(int)Math.Round(i.Mean, MidpointRounding.AwayFromZero),
                          (int?)(int)Math.Round(i.Weight, MidpointRounding.AwayFromZero)))
            .ToList();
        double? itemSe = BenchmarkScoring.QualityIndexStandardError(itemInputs);
        double? itemHalf = itemSe.HasValue ? NormalCritical95 * itemSe.Value : null;

        double? combined = null;
        if (reproHalf.HasValue && itemHalf.HasValue)
        {
            combined = Math.Sqrt(reproHalf.Value * reproHalf.Value + itemHalf.Value * itemHalf.Value);
        }
        else if (itemHalf.HasValue)
        {
            combined = itemHalf.Value;
        }
        else if (reproHalf.HasValue)
        {
            combined = reproHalf.Value;
        }

        double? storedMean = members.Where(m => m.QualityIndex.HasValue).Select(m => (double)m.QualityIndex!.Value)
            .DefaultIfEmpty(double.NaN).Average();
        if (storedMean.HasValue && double.IsNaN(storedMean.Value)) storedMean = null;

        return new BenchmarkGroupIndexStatistics
        {
            RunCount = r,
            PointEstimate = point,
            WeightedMeanOfItemMeans = weightedItemMean,
            IdentityHolds = Math.Abs(point - weightedItemMean) < 1e-9,
            PerRunIndices = perRunIndices,
            MeanStoredQualityIndex = storedMean,
            ReproducibilityStandardDeviation = reproSd,
            ReproducibilityStandardError = reproSe,
            ReproducibilityCriticalValue = tCrit,
            ReproducibilityHalfWidth = reproHalf,
            ItemSamplingStandardError = itemSe,
            ItemSamplingHalfWidth = itemHalf,
            CombinedHalfWidth = combined,
            CombinedLower = combined.HasValue ? ClampScore(point - combined.Value) : null,
            CombinedUpper = combined.HasValue ? ClampScore(point + combined.Value) : null,
            CombinedIntervalTruncated = combined.HasValue && ScoreBoundExceeded(point, combined.Value),
            ReproducibilityAvailable = reproSd.HasValue
        };
    }

    /// <summary>
    /// Confines a reported interval bound to the score range. A quality score is bounded [0, 100],
    /// so a bound outside it is not a wider claim but an impossible one — an item scoring 71 with a
    /// half-width of 58 does not have an upper bound of 129.
    /// </summary>
    private static double ClampScore(double value) => Math.Clamp(value, MinScore, MaxScore);

    /// <summary>Whether either end of <c>centre ± halfWidth</c> leaves the score range.</summary>
    private static bool ScoreBoundExceeded(double centre, double halfWidth)
        => centre - halfWidth < MinScore || centre + halfWidth > MaxScore;

    private static BenchmarkGroupSpeedStatistics ComputeSpeed(
        IReadOnlyList<BenchmarkRun> members,
        IReadOnlyList<BenchmarkGroupItemStatistics> items,
        BenchmarkGroupStatisticsOptions cfg)
    {
        var speedIndices = members
            .Where(m => m.SpeedIndex.HasValue)
            .Select(m => (double)m.SpeedIndex!.Value)
            .ToList();

        var questionIds = new HashSet<long>(items.Select(i => i.QuestionId));
        var pooled = members
            .SelectMany(m => m.Answers ?? new List<BenchmarkRunAnswer>())
            .Where(a => a.Status == BenchmarkAnswerStatus.Ok
                        && a.BenchmarkQuestionId.HasValue
                        && questionIds.Contains(a.BenchmarkQuestionId.Value))
            .Select(a => (double)a.ModelTimeMs)
            .ToList();

        return new BenchmarkGroupSpeedStatistics
        {
            RunCount = members.Count,
            MeanSpeedIndex = speedIndices.Count > 0 ? speedIndices.Average() : null,
            SpeedIndexStandardDeviation = SampleStandardDeviation(speedIndices),
            PerRunSpeedIndices = speedIndices,
            PooledAnswerCount = pooled.Count,
            ModelTimeP50Ms = Percentile(pooled, 50.0),
            ModelTimeP90Ms = Percentile(pooled, 90.0),
            ModelTimeMaxMs = pooled.Count > 0 ? pooled.Max() : null,
            Degraded = cfg.SpeedDegraded,
            DegradedReason = cfg.SpeedDegradedReason,
            Caveat = BuildSpeedCaveat(cfg)
        };
    }

    /// <summary>
    /// The four scoring dimensions across the members.
    ///
    /// Restricted to answered items in the group, exactly as the speed pool is: an item no member
    /// answered is not part of the group's item set, so its absent dimension scores must not enter
    /// a mean either. Every dimension is emitted whether or not it was scored.
    /// </summary>
    private static IReadOnlyList<BenchmarkGroupDimensionStatistics> ComputeDimensions(
        IReadOnlyList<BenchmarkRun> members,
        IReadOnlyList<BenchmarkGroupItemStatistics> items)
    {
        var questionIds = new HashSet<long>(items.Select(i => i.QuestionId));

        var selectors = new (string Name, Func<BenchmarkRunAnswer, int?> Score)[]
        {
            ("Accuracy", a => a.AccuracyScore),
            ("Completeness", a => a.CompletenessScore),
            ("Conciseness", a => a.ConcisenessScore),
            ("Readability", a => a.ReadabilityScore)
        };

        var result = new List<BenchmarkGroupDimensionStatistics>(selectors.Length);

        foreach (var (name, score) in selectors)
        {
            var perRunMeans = new List<double>();
            var byQuestion = new Dictionary<long, List<double>>();

            foreach (var member in members)
            {
                var scored = (member.Answers ?? new List<BenchmarkRunAnswer>())
                    .Where(a => a.Status == BenchmarkAnswerStatus.Ok
                                && a.BenchmarkQuestionId.HasValue
                                && questionIds.Contains(a.BenchmarkQuestionId.Value)
                                && score(a).HasValue)
                    .ToList();

                if (scored.Count == 0) continue;

                perRunMeans.Add(scored.Average(a => (double)score(a)!.Value));

                foreach (var answer in scored)
                {
                    long questionId = answer.BenchmarkQuestionId!.Value;
                    if (!byQuestion.TryGetValue(questionId, out var list))
                    {
                        list = new List<double>();
                        byQuestion[questionId] = list;
                    }

                    list.Add(score(answer)!.Value);
                }
            }

            double? sd = SampleStandardDeviation(perRunMeans);
            double? half = null;
            if (sd.HasValue && perRunMeans.Count >= MinRunsForReproducibility)
            {
                double se = sd.Value / Math.Sqrt(perRunMeans.Count);
                half = StudentTCritical95(perRunMeans.Count - 1) * se;
            }

            result.Add(new BenchmarkGroupDimensionStatistics
            {
                Dimension = name,
                PerRunMeans = perRunMeans,
                Mean = perRunMeans.Count > 0 ? perRunMeans.Average() : null,
                StandardDeviation = sd,
                ConfidenceHalfWidth = half,
                Min = perRunMeans.Count > 0 ? perRunMeans.Min() : null,
                Max = perRunMeans.Count > 0 ? perRunMeans.Max() : null,
                ItemMeans = byQuestion.ToDictionary(kv => kv.Key, kv => kv.Value.Average())
            });
        }

        return result;
    }

    /// <summary>
    /// Token, tool and claim-verification totals pooled over the members.
    ///
    /// Returns null when no member recorded any of it, which every run predating the token columns
    /// is: an all-zero usage block reads as a measurement, and it is not one.
    /// </summary>
    private static BenchmarkGroupUsageStatistics? ComputeUsage(
        IReadOnlyList<BenchmarkRun> members,
        IReadOnlyList<BenchmarkGroupItemStatistics> items)
    {
        if (members.Count == 0) return null;

        var questionIds = new HashSet<long>(items.Select(i => i.QuestionId));

        long input = members.Sum(m => m.TotalInputTokens);
        long output = members.Sum(m => m.TotalOutputTokens);
        long cacheRead = members.Sum(m => m.TotalCacheReadTokens);

        // Tool counts come from the per-answer summaries through the same classifier the single-run
        // report uses, so a family share means the same thing on both surfaces.
        var answers = members
            .SelectMany(m => m.Answers ?? new List<BenchmarkRunAnswer>())
            .Where(a => a.Status == BenchmarkAnswerStatus.Ok
                        && a.BenchmarkQuestionId.HasValue
                        && questionIds.Contains(a.BenchmarkQuestionId.Value))
            .ToList();

        var byFamily = new Dictionary<string, int>(StringComparer.Ordinal);
        int totalToolCalls = 0;
        foreach (var (tool, count) in BenchmarkChatTransfer.AggregateToolCounts(answers))
        {
            string family = BenchmarkChatTransfer.ClassifyTool(tool).ToString();
            byFamily[family] = byFamily.GetValueOrDefault(family) + count;
            totalToolCalls += count;
        }

        var perRunToolCalls = members
            .Select(m => (double)BenchmarkChatTransfer.AggregateToolCounts(
                (m.Answers ?? new List<BenchmarkRunAnswer>())
                    .Where(a => a.Status == BenchmarkAnswerStatus.Ok)).Values.Sum())
            .ToList();

        int supported = members.Sum(m => m.ClaimsSupportedCount);
        int refuted = members.Sum(m => m.ClaimsRefutedCount);
        int indeterminate = members.Sum(m => m.ClaimsIndeterminateCount);

        var usage = new BenchmarkGroupUsageStatistics
        {
            RunCount = members.Count,
            TotalInputTokens = input,
            TotalOutputTokens = output,
            TotalCacheReadTokens = cacheRead,
            CacheReadSharePercentage = input > 0 ? cacheRead * 100.0 / input : null,
            InputOutputRatio = output > 0 ? input / (double)output : null,
            PerRunInputTokens = members.Select(m => m.TotalInputTokens).ToList(),
            InputTokenStandardDeviation =
                SampleStandardDeviation(members.Select(m => (double)m.TotalInputTokens).ToList()),
            TotalAssessmentInputTokens = members.Sum(m => m.TotalAssessmentInputTokens),
            TotalAssessmentOutputTokens = members.Sum(m => m.TotalAssessmentOutputTokens),
            TotalClaimVerificationInputTokens = members.Sum(m => m.TotalClaimVerificationInputTokens),
            TotalClaimVerificationOutputTokens = members.Sum(m => m.TotalClaimVerificationOutputTokens),
            TotalToolCalls = totalToolCalls,
            MeanToolCallsPerRun = perRunToolCalls.Count > 0 ? perRunToolCalls.Average() : null,
            ToolCallStandardDeviation = SampleStandardDeviation(perRunToolCalls),
            ToolCallsByFamily = byFamily,
            ToolFamilyShares = totalToolCalls > 0
                ? byFamily.ToDictionary(kv => kv.Key, kv => kv.Value * 100.0 / totalToolCalls, StringComparer.Ordinal)
                : new Dictionary<string, double>(StringComparer.Ordinal),
            ClaimsSupported = supported,
            ClaimsRefuted = refuted,
            ClaimsIndeterminate = indeterminate,
            ClaimsChecked = supported + refuted + indeterminate,
            AnswersWithVerification = members.Sum(m => m.ClaimVerifiedAnswerCount)
        };

        bool anythingRecorded = input > 0 || output > 0 || totalToolCalls > 0
            || usage.ClaimsChecked > 0 || usage.AnswersWithVerification > 0
            || usage.TotalAssessmentInputTokens > 0 || usage.TotalClaimVerificationInputTokens > 0;

        return anythingRecorded ? usage : null;
    }

    /// <summary>
    /// The prompt configuration the members were graded under, decoded from the first member.
    ///
    /// The comparability key covers the prompt options, so a poolable group cannot disagree on
    /// them; <c>Divergent</c> checks that rather than trusting it, because a group assembled by
    /// hand at Tier C can.
    /// </summary>
    private static BenchmarkGroupPromptUnderTest? ComputePromptUnderTest(
        IReadOnlyList<BenchmarkRun> members)
    {
        if (members.Count == 0) return null;

        var first = members[0];
        bool recorded = !string.IsNullOrWhiteSpace(first.CandidatePromptOptionsJson);
        var options = BenchmarkCandidatePromptOptions.FromJson(first.CandidatePromptOptionsJson);

        string signature = options.ComparabilitySignature(first.TestedModelParallelExecutionModeUsed);
        bool divergent = members.Skip(1).Any(m =>
            BenchmarkCandidatePromptOptions.FromJson(m.CandidatePromptOptionsJson)
                .ComparabilitySignature(m.TestedModelParallelExecutionModeUsed) != signature);

        return new BenchmarkGroupPromptUnderTest
        {
            Recorded = recorded,
            Divergent = divergent,
            OverseerMode = options.OverseerMode,
            VerboseMode = options.VerboseMode,
            SpoilerFreeMode = options.SpoilerFreeMode,
            EnableToolUse = options.EnableToolUse,
            EnableWebSearch = options.EnableWebSearch,
            EnableSubAgents = options.EnableSubAgents,
            AllowSourceCodeReferences = options.AllowSourceCodeReferences,
            IsGameOn = options.IsGameOn,
            DeveloperMode = options.DeveloperMode,
            HasMessageHistory = options.HasMessageHistory,
            HasWikiContext = options.HasWikiContext,
            HasGameSnapshot = options.HasGameSnapshot,
            ParallelMode = first.TestedModelParallelExecutionModeUsed
        };
    }

    /// <summary>
    /// The speed caveat for one group, built from that group's own degradation state.
    ///
    /// A tier is not a timing verdict: a group can sit at Tier B on a key that degrades cost alone
    /// and have its speed figures measured under one condition throughout.
    /// </summary>
    private static string BuildSpeedCaveat(BenchmarkGroupStatisticsOptions cfg)
    {
        if (!cfg.SpeedDegraded)
        {
            return SpeedCaveat + SpeedNotDegradedCaveat;
        }

        string reason = string.IsNullOrWhiteSpace(cfg.SpeedDegradedReason)
            ? "the comparability keys covering timing differ across the members."
            : cfg.SpeedDegradedReason!.Trim();

        return SpeedCaveat + SpeedDegradedCaveatPrefix + reason;
    }

    private static BenchmarkGroupCostStatistics? ComputeCost(
        IReadOnlyCollection<BenchmarkGroupRunCost>? costs,
        int itemCount,
        double pointEstimate,
        BenchmarkGroupStatisticsOptions cfg)
    {
        if (costs == null || costs.Count == 0) return null;

        var totals = costs.Select(c => c.Total).ToList();
        double total = totals.Sum();
        double meanPerRun = totals.Average();

        var byRole = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var cost in costs)
        {
            foreach (var kv in cost.CostByRole)
            {
                byRole[kv.Key] = byRole.GetValueOrDefault(kv.Key) + kv.Value;
            }
        }

        var meanByRole = byRole.ToDictionary(kv => kv.Key, kv => kv.Value / costs.Count, StringComparer.Ordinal);

        // Per-role dispersion. A role missing from one member's dictionary contributes 0.0 rather
        // than being skipped: a run that spent nothing on a role did spend nothing, and dropping the
        // sample would understate the spread of the role that actually varies.
        var sdByRole = new Dictionary<string, double?>(StringComparer.Ordinal);
        var minByRole = new Dictionary<string, double>(StringComparer.Ordinal);
        var maxByRole = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (string role in byRole.Keys)
        {
            var perRun = costs.Select(c => c.CostByRole.TryGetValue(role, out double v) ? v : 0.0).ToList();
            sdByRole[role] = SampleStandardDeviation(perRun);
            minByRole[role] = perRun.Min();
            maxByRole[role] = perRun.Max();
        }

        return new BenchmarkGroupCostStatistics
        {
            RunCount = costs.Count,
            TotalCost = total,
            MeanCostPerRun = meanPerRun,
            CostStandardDeviation = SampleStandardDeviation(totals),
            TotalCostByRole = byRole,
            MeanCostByRole = meanByRole,
            PerRunTotals = totals,
            CostStandardDeviationByRole = sdByRole,
            MinCostByRole = minByRole,
            MaxCostByRole = maxByRole,
            CostPerQuestion = itemCount > 0 ? meanPerRun / itemCount : null,
            CostPerIndexPoint = pointEstimate > 0.0 ? meanPerRun / pointEstimate : null,
            Degraded = cfg.CostDegraded,
            DegradedReason = cfg.CostDegradedReason
        };
    }

    // --- Group comparison ---------------------------------------------------------------------

    /// <summary>
    /// Compares two groups, paired by question on per-item cross-run mean quality. Differences are
    /// <b>treatment minus baseline</b>, so a positive mean difference means the treatment scored
    /// higher.
    ///
    /// Only items answered on both sides are paired; the rest are counted and excluded, because a
    /// pair needs two halves and imputing one would invent the finding.
    /// </summary>
    public static BenchmarkGroupComparison Compare(
        BenchmarkGroupStatisticsResult baseline,
        BenchmarkGroupStatisticsResult treatment,
        double falseDiscoveryRate = DefaultFalseDiscoveryRate)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(treatment);

        var baseByQuestion = baseline.Items.ToDictionary(i => i.QuestionId);
        var treatByQuestion = treatment.Items.ToDictionary(i => i.QuestionId);

        var pairedIds = baseByQuestion.Keys.Intersect(treatByQuestion.Keys).ToList();
        int unpaired = baseByQuestion.Count + treatByQuestion.Count - 2 * pairedIds.Count;

        var ordered = pairedIds
            .Select(id => (Baseline: baseByQuestion[id], Treatment: treatByQuestion[id]))
            .OrderBy(p => p.Baseline.OrderIndex)
            .ToList();

        var differences = ordered.Select(p => p.Treatment.Mean - p.Baseline.Mean).ToList();

        double meanDiff = differences.Count > 0 ? differences.Average() : 0.0;
        double? diffSd = SampleStandardDeviation(differences);
        double? halfWidth = null;
        if (diffSd.HasValue && differences.Count >= 2)
        {
            halfWidth = StudentTCritical95(differences.Count - 1) * diffSd.Value / Math.Sqrt(differences.Count);
        }

        var wilcoxon = WilcoxonSignedRank(differences);
        var pairedT = PairedTTest(differences);
        double? dz = CohensDz(differences);

        // Per-item exploratory tests: Welch's t over the two groups' per-run scores for the item,
        // then Benjamini-Hochberg across the items that could be tested at all.
        var rawItems = new List<BenchmarkGroupItemComparison>();
        foreach (var (b, t) in ordered)
        {
            var welch = WelchTTest(b.Scores, t.Scores);
            rawItems.Add(new BenchmarkGroupItemComparison
            {
                QuestionId = b.QuestionId,
                OrderIndex = b.OrderIndex,
                QuestionText = b.QuestionText,
                BaselineRunCount = b.RunCount,
                TreatmentRunCount = t.RunCount,
                BaselineMean = b.Mean,
                TreatmentMean = t.Mean,
                Difference = t.Mean - b.Mean,
                TStatistic = welch.TStatistic,
                DegreesOfFreedom = welch.DegreesOfFreedom,
                PValue = welch.PValue
            });
        }

        var testable = rawItems.Where(i => i.PValue.HasValue).ToList();
        var fdr = BenjaminiHochberg(testable.Select(i => i.PValue!.Value).ToList(), falseDiscoveryRate);
        var byTestableIndex = fdr.ToDictionary(f => f.Index);

        var itemComparisons = new List<BenchmarkGroupItemComparison>(rawItems.Count);
        int testableCursor = 0;
        foreach (var item in rawItems)
        {
            if (!item.PValue.HasValue)
            {
                itemComparisons.Add(item);
                continue;
            }

            var adjusted = byTestableIndex[testableCursor++];
            itemComparisons.Add(item with
            {
                AdjustedPValue = adjusted.AdjustedPValue,
                RejectedAtFdr = adjusted.Rejected
            });
        }

        return new BenchmarkGroupComparison
        {
            BaselineRunIds = baseline.RunIds,
            TreatmentRunIds = treatment.RunIds,
            PairedItemCount = ordered.Count,
            UnpairedItemCount = unpaired,
            MeanDifference = meanDiff,
            DifferenceStandardDeviation = diffSd,
            DifferenceConfidenceHalfWidth = halfWidth,
            DifferenceConfidenceLower = halfWidth.HasValue ? meanDiff - halfWidth.Value : null,
            DifferenceConfidenceUpper = halfWidth.HasValue ? meanDiff + halfWidth.Value : null,
            Wilcoxon = wilcoxon,
            PairedT = pairedT,
            CohensDz = dz,
            ItemComparisons = itemComparisons,
            FalseDiscoveryRate = falseDiscoveryRate
        };
    }

    // --- Tests and effect sizes -----------------------------------------------------------------

    /// <summary>
    /// The Wilcoxon signed-rank test over paired differences.
    ///
    /// Procedure, stated because every step of it has a defensible alternative:
    /// <list type="number">
    /// <item>Differences of exactly zero are <b>discarded</b> before ranking — Wilcoxon's original
    /// reduction, not Pratt's — and their count is reported, because discarding them lowers the
    /// effective sample size.</item>
    /// <item>The absolute differences are ranked ascending with <b>average ranks for ties</b>.</item>
    /// <item><i>W</i>+ and <i>W</i>− are the rank sums of the positive and negative differences;
    /// the statistic is <c>min(W+, W−)</c>.</item>
    /// <item>At or below <see cref="MaxExactWilcoxonSampleSize"/> non-zero pairs the p-value is
    /// <b>exact</b>: the conditional permutation distribution over the observed ranks, enumerated
    /// by dynamic programming over doubled rank sums so that fractional tie ranks stay integral.
    /// Above it, a <b>normal approximation</b> with the standard tie correction
    /// <c>Σ(t³ − t) / 48</c> and a continuity correction of 0.5 toward the mean.</item>
    /// </list>
    ///
    /// The p-value is two-sided. Null when no non-zero pair survives step 1, where there is nothing
    /// to test rather than a result of "no difference".
    /// </summary>
    public static BenchmarkWilcoxonSignedRankResult WilcoxonSignedRank(IReadOnlyList<double>? differences)
    {
        var all = differences ?? Array.Empty<double>();
        int zeros = all.Count(d => d == 0.0);
        var nonZero = all.Where(d => d != 0.0).ToList();
        int n = nonZero.Count;

        if (n == 0)
        {
            return new BenchmarkWilcoxonSignedRankResult
            {
                SampleSize = 0,
                ZeroDifferenceCount = zeros,
                Method = "not computed"
            };
        }

        var absolute = nonZero.Select(Math.Abs).ToList();
        var ranks = AverageRanks(absolute);
        bool ties = ranks.Any(r => Math.Abs(r - Math.Round(r)) > 1e-9)
                    || absolute.Distinct().Count() != absolute.Count;

        double positive = 0.0;
        double negative = 0.0;
        for (int i = 0; i < n; i++)
        {
            if (nonZero[i] > 0.0) positive += ranks[i];
            else negative += ranks[i];
        }

        double statistic = Math.Min(positive, negative);

        double? p;
        string method;
        if (n <= MaxExactWilcoxonSampleSize)
        {
            p = ExactWilcoxonTwoSidedPValue(ranks, statistic);
            method = "exact (conditional permutation over the observed ranks)";
        }
        else
        {
            p = NormalWilcoxonTwoSidedPValue(positive, n, absolute);
            method = "normal approximation (tie-corrected, continuity-corrected)";
        }

        return new BenchmarkWilcoxonSignedRankResult
        {
            SampleSize = n,
            ZeroDifferenceCount = zeros,
            PositiveRankSum = positive,
            NegativeRankSum = negative,
            Statistic = statistic,
            PValue = p,
            Method = method,
            TiesPresent = ties
        };
    }

    /// <summary>
    /// A paired <i>t</i>-test over the differences: <c>t = mean / (SD / √n)</c> on <i>n</i>−1
    /// degrees of freedom, two-sided. Null throughout below two pairs, and null when the
    /// differences have zero spread, where <i>t</i> is undefined rather than infinite.
    /// </summary>
    public static BenchmarkPairedTTestResult PairedTTest(IReadOnlyList<double>? differences)
    {
        var d = differences ?? Array.Empty<double>();
        if (d.Count == 0)
        {
            return new BenchmarkPairedTTestResult();
        }

        double mean = d.Average();
        double? sd = SampleStandardDeviation(d);
        if (!sd.HasValue || sd.Value <= 0.0)
        {
            return new BenchmarkPairedTTestResult { SampleSize = d.Count, MeanDifference = mean };
        }

        double se = sd.Value / Math.Sqrt(d.Count);
        double t = mean / se;
        double df = d.Count - 1;

        return new BenchmarkPairedTTestResult
        {
            SampleSize = d.Count,
            MeanDifference = mean,
            StandardError = se,
            TStatistic = t,
            DegreesOfFreedom = df,
            PValue = StudentTTwoSidedPValue(t, df)
        };
    }

    /// <summary>
    /// Cohen's <i>d</i><sub>z</sub> for a paired design: the mean of the differences over their
    /// sample standard deviation. Null below two pairs, and null at zero spread.
    /// </summary>
    public static double? CohensDz(IReadOnlyList<double>? differences)
    {
        var d = differences ?? Array.Empty<double>();
        double? sd = SampleStandardDeviation(d);
        if (!sd.HasValue || sd.Value <= 0.0) return null;
        return d.Average() / sd.Value;
    }

    /// <summary>
    /// The Benjamini–Hochberg step-up procedure at a false discovery rate of
    /// <paramref name="falseDiscoveryRate"/>.
    ///
    /// Sort the <i>m</i> p-values ascending; find the largest <i>k</i> with
    /// <c>p(k) ≤ k/m · q</c>; reject every hypothesis with a p-value at or below <c>p(k)</c>. The
    /// adjusted p-value returned alongside is the standard monotone one,
    /// <c>q(i) = min over j ≥ i of (m/j · p(j))</c>, clamped to 1 — so a caller may either read the
    /// rejection flag or threshold the adjusted values and get the same answer.
    ///
    /// Results come back in the input order, each carrying its original index.
    /// </summary>
    public static IReadOnlyList<BenchmarkFdrResult> BenjaminiHochberg(
        IReadOnlyList<double>? pValues,
        double falseDiscoveryRate = DefaultFalseDiscoveryRate)
    {
        var p = pValues ?? Array.Empty<double>();
        int m = p.Count;
        if (m == 0) return Array.Empty<BenchmarkFdrResult>();

        var order = Enumerable.Range(0, m).OrderBy(i => p[i]).ToList();

        // Step-up adjusted p-values, walking down from the largest.
        var adjusted = new double[m];
        double running = 1.0;
        for (int rank = m; rank >= 1; rank--)
        {
            int idx = order[rank - 1];
            double candidate = p[idx] * m / rank;
            running = Math.Min(running, candidate);
            adjusted[idx] = Math.Min(1.0, running);
        }

        // Largest k with p(k) <= k/m * q.
        int cutoff = 0;
        for (int rank = m; rank >= 1; rank--)
        {
            if (p[order[rank - 1]] <= rank / (double)m * falseDiscoveryRate)
            {
                cutoff = rank;
                break;
            }
        }

        var rejected = new bool[m];
        for (int rank = 1; rank <= cutoff; rank++)
        {
            rejected[order[rank - 1]] = true;
        }

        return Enumerable.Range(0, m)
            .Select(i => new BenchmarkFdrResult
            {
                Index = i,
                PValue = p[i],
                AdjustedPValue = adjusted[i],
                Rejected = rejected[i]
            })
            .ToList();
    }

    /// <summary>
    /// Welch's unequal-variance <i>t</i>-test over two independent samples, two-sided. Used only
    /// for the exploratory per-item comparisons.
    ///
    /// Null when either side has fewer than two values. When both sides have zero variance the
    /// test degenerates: identical means give p = 1, different means give p = 0, which is the
    /// honest reading of "no observed variation and a difference" rather than a division by zero.
    /// </summary>
    public static (double? TStatistic, double? DegreesOfFreedom, double? PValue) WelchTTest(
        IReadOnlyList<double>? a,
        IReadOnlyList<double>? b)
    {
        var x = a ?? Array.Empty<double>();
        var y = b ?? Array.Empty<double>();
        if (x.Count < 2 || y.Count < 2) return (null, null, null);

        double mx = x.Average();
        double my = y.Average();
        double vx = SampleVariance(x)!.Value;
        double vy = SampleVariance(y)!.Value;

        double sx = vx / x.Count;
        double sy = vy / y.Count;
        double se2 = sx + sy;

        if (se2 <= 0.0)
        {
            double diff = my - mx;
            return (null, null, Math.Abs(diff) < 1e-12 ? 1.0 : 0.0);
        }

        double se = Math.Sqrt(se2);
        double t = (my - mx) / se;
        double df = se2 * se2 / (sx * sx / (x.Count - 1) + sy * sy / (y.Count - 1));

        return (t, df, StudentTTwoSidedPValue(t, df));
    }

    // --- Descriptive primitives -----------------------------------------------------------------

    /// <summary>Sample variance with the <i>n</i>−1 denominator. Null below two values.</summary>
    public static double? SampleVariance(IReadOnlyList<double>? values)
    {
        var v = values ?? Array.Empty<double>();
        if (v.Count < 2) return null;

        double mean = v.Average();
        double sum = v.Sum(x => (x - mean) * (x - mean));
        return sum / (v.Count - 1);
    }

    /// <summary>Sample standard deviation with the <i>n</i>−1 denominator. Null below two values.</summary>
    public static double? SampleStandardDeviation(IReadOnlyList<double>? values)
    {
        var variance = SampleVariance(values);
        return variance.HasValue ? Math.Sqrt(variance.Value) : null;
    }

    /// <summary>Median. Null over an empty sequence.</summary>
    public static double? Median(IReadOnlyList<double>? values)
    {
        var v = values ?? Array.Empty<double>();
        if (v.Count == 0) return null;

        var sorted = v.OrderBy(x => x).ToList();
        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 1
            ? sorted[mid]
            : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }

    /// <summary>
    /// The <paramref name="percentile"/>th percentile by linear interpolation between order
    /// statistics: rank <c>p/100 · (n−1)</c>, interpolated between its floor and ceiling. This is
    /// the definition NumPy and most spreadsheets use, chosen so that P50 equals
    /// <see cref="Median"/> exactly. Null over an empty sequence.
    /// </summary>
    public static double? Percentile(IReadOnlyList<double>? values, double percentile)
    {
        var v = values ?? Array.Empty<double>();
        if (v.Count == 0) return null;
        if (v.Count == 1) return v[0];

        var sorted = v.OrderBy(x => x).ToList();
        double rank = Math.Clamp(percentile, 0.0, 100.0) / 100.0 * (sorted.Count - 1);
        int lo = (int)Math.Floor(rank);
        int hi = (int)Math.Ceiling(rank);
        if (lo == hi) return sorted[lo];

        return sorted[lo] + (rank - lo) * (sorted[hi] - sorted[lo]);
    }

    // --- Distributions --------------------------------------------------------------------------

    // Two-sided 95% critical values of Student's t, df 1..30. Tabulated rather than computed
    // because the Cornish-Fisher expansion below is poor at very small df, which is exactly where
    // a replicate set lives: R = 3 means df = 2.
    private static readonly double[] TTable95 =
    {
        double.NaN,
        12.7062, 4.3027, 3.1824, 2.7764, 2.5706, 2.4469, 2.3646, 2.3060, 2.2622, 2.2281,
        2.2010, 2.1788, 2.1604, 2.1448, 2.1314, 2.1199, 2.1098, 2.1009, 2.0930, 2.0860,
        2.0796, 2.0739, 2.0687, 2.0639, 2.0595, 2.0555, 2.0518, 2.0484, 2.0452, 2.0423
    };

    /// <summary>
    /// The two-sided 95 % critical value of Student's <i>t</i> on
    /// <paramref name="degreesOfFreedom"/> degrees of freedom. Tabulated to df = 30 and continued
    /// by the Cornish–Fisher expansion above it, which agrees with published tables to four
    /// decimals from df = 31 upward. Returns the normal value 1.96 at df ≤ 0, where no <i>t</i>
    /// interval is defined.
    /// </summary>
    public static double StudentTCritical95(int degreesOfFreedom)
    {
        if (degreesOfFreedom <= 0) return NormalCritical95;
        if (degreesOfFreedom < TTable95.Length) return TTable95[degreesOfFreedom];

        double z = NormalCritical95;
        double df = degreesOfFreedom;
        return z
            + (z * z * z + z) / (4.0 * df)
            + (5.0 * Math.Pow(z, 5) + 16.0 * z * z * z + 3.0 * z) / (96.0 * df * df);
    }

    /// <summary>
    /// Two-sided p-value of Student's <i>t</i>: <c>I_{df/(df+t²)}(df/2, 1/2)</c>, from the
    /// regularized incomplete beta function.
    /// </summary>
    public static double StudentTTwoSidedPValue(double t, double degreesOfFreedom)
    {
        if (degreesOfFreedom <= 0.0 || double.IsNaN(t)) return double.NaN;
        if (double.IsInfinity(t)) return 0.0;

        double x = degreesOfFreedom / (degreesOfFreedom + t * t);
        return Math.Clamp(RegularizedIncompleteBeta(degreesOfFreedom / 2.0, 0.5, x), 0.0, 1.0);
    }

    /// <summary>The standard normal cumulative distribution function.</summary>
    public static double NormalCdf(double z) => 0.5 * Erfc(-z / Math.Sqrt(2.0));

    // --- Internals ------------------------------------------------------------------------------

    /// <summary>Ascending ranks of <paramref name="values"/>, with average ranks for ties.</summary>
    private static double[] AverageRanks(IReadOnlyList<double> values)
    {
        int n = values.Count;
        var order = Enumerable.Range(0, n).OrderBy(i => values[i]).ToArray();
        var ranks = new double[n];

        int i2 = 0;
        while (i2 < n)
        {
            int j = i2;
            while (j + 1 < n && values[order[j + 1]] == values[order[i2]]) j++;

            double average = (i2 + j + 2) / 2.0;   // ranks are 1-based: (i+1 .. j+1) averaged
            for (int k = i2; k <= j; k++)
            {
                ranks[order[k]] = average;
            }

            i2 = j + 1;
        }

        return ranks;
    }

    /// <summary>
    /// The exact two-sided p-value from the conditional permutation distribution: every one of the
    /// 2^n sign assignments of the observed ranks is equally likely under the null, so
    /// <c>P(W+ ≤ w)</c> is counted by dynamic programming over rank sums. Ranks are doubled first
    /// so that fractional tie ranks stay integral.
    /// </summary>
    private static double ExactWilcoxonTwoSidedPValue(double[] ranks, double statistic)
    {
        int n = ranks.Length;

        var doubled = ranks.Select(r => (int)Math.Round(r * 2.0)).ToArray();
        int total = doubled.Sum();
        int target = (int)Math.Round(statistic * 2.0);

        // counts[s] = number of subsets of the ranks whose doubled sum is s.
        var counts = new double[total + 1];
        counts[0] = 1.0;
        int reach = 0;
        foreach (int r in doubled)
        {
            reach += r;
            for (int s = reach; s >= r; s--)
            {
                counts[s] += counts[s - r];
            }
        }

        double atOrBelow = 0.0;
        for (int s = 0; s <= target && s <= total; s++)
        {
            atOrBelow += counts[s];
        }

        double tail = atOrBelow / Math.Pow(2.0, n);
        return Math.Min(1.0, 2.0 * tail);
    }

    /// <summary>
    /// The normal approximation with the standard tie correction and a 0.5 continuity correction
    /// toward the mean: <c>μ = n(n+1)/4</c>, <c>σ² = n(n+1)(2n+1)/24 − Σ(t³−t)/48</c>.
    /// </summary>
    private static double? NormalWilcoxonTwoSidedPValue(double positiveRankSum, int n, IReadOnlyList<double> absolute)
    {
        double mu = n * (n + 1) / 4.0;
        double variance = n * (n + 1.0) * (2.0 * n + 1.0) / 24.0;

        foreach (var group in absolute.GroupBy(v => v))
        {
            double t = group.Count();
            variance -= (t * t * t - t) / 48.0;
        }

        if (variance <= 0.0) return null;

        double diff = positiveRankSum - mu;
        double corrected = diff > 0 ? diff - 0.5 : diff + 0.5;
        if (Math.Abs(diff) < 0.5) corrected = 0.0;

        double z = corrected / Math.Sqrt(variance);
        return Math.Clamp(2.0 * (1.0 - NormalCdf(Math.Abs(z))), 0.0, 1.0);
    }

    /// <summary>
    /// The regularized incomplete beta function <c>I_x(a, b)</c>, by the Lentz continued fraction.
    /// The one special function this file needs: it gives Student's <i>t</i> tail exactly, which a
    /// normal approximation would not at the sample sizes a benchmark suite produces.
    /// </summary>
    public static double RegularizedIncompleteBeta(double a, double b, double x)
    {
        if (x <= 0.0) return 0.0;
        if (x >= 1.0) return 1.0;

        double front = Math.Exp(LogGamma(a + b) - LogGamma(a) - LogGamma(b)
                                + a * Math.Log(x) + b * Math.Log(1.0 - x));

        return x < (a + 1.0) / (a + b + 2.0)
            ? front * BetaContinuedFraction(a, b, x) / a
            : 1.0 - front * BetaContinuedFraction(b, a, 1.0 - x) / b;
    }

    private static double BetaContinuedFraction(double a, double b, double x)
    {
        const double Tiny = 1e-30;
        const double Epsilon = 1e-14;
        const int MaxIterations = 500;

        double qab = a + b;
        double qap = a + 1.0;
        double qam = a - 1.0;
        double c = 1.0;
        double d = 1.0 - qab * x / qap;
        if (Math.Abs(d) < Tiny) d = Tiny;
        d = 1.0 / d;
        double h = d;

        for (int m = 1; m <= MaxIterations; m++)
        {
            int m2 = 2 * m;

            double aa = m * (b - m) * x / ((qam + m2) * (a + m2));
            d = 1.0 + aa * d;
            if (Math.Abs(d) < Tiny) d = Tiny;
            c = 1.0 + aa / c;
            if (Math.Abs(c) < Tiny) c = Tiny;
            d = 1.0 / d;
            h *= d * c;

            aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2));
            d = 1.0 + aa * d;
            if (Math.Abs(d) < Tiny) d = Tiny;
            c = 1.0 + aa / c;
            if (Math.Abs(c) < Tiny) c = Tiny;
            d = 1.0 / d;

            double del = d * c;
            h *= del;

            if (Math.Abs(del - 1.0) < Epsilon) break;
        }

        return h;
    }

    private static readonly double[] LanczosCoefficients =
    {
        676.5203681218851, -1259.1392167224028, 771.32342877765313,
        -176.61502916214059, 12.507343278686905, -0.13857109526572012,
        9.9843695780195716e-6, 1.5056327351493116e-7
    };

    /// <summary>The log of the gamma function, by the Lanczos approximation (g = 7, n = 9).</summary>
    private static double LogGamma(double x)
    {
        if (x < 0.5)
        {
            return Math.Log(Math.PI / Math.Sin(Math.PI * x)) - LogGamma(1.0 - x);
        }

        x -= 1.0;
        double a = 0.99999999999980993;
        double t = x + 7.5;
        for (int i = 0; i < LanczosCoefficients.Length; i++)
        {
            a += LanczosCoefficients[i] / (x + i + 1);
        }

        return 0.5 * Math.Log(2.0 * Math.PI) + (x + 0.5) * Math.Log(t) - t + Math.Log(a);
    }

    /// <summary>
    /// The complementary error function, by the Chebyshev-fitted rational form with a relative
    /// error below 1.2e-7 — far tighter than any p-value here is read to.
    /// </summary>
    private static double Erfc(double x)
    {
        double z = Math.Abs(x);
        double t = 2.0 / (2.0 + z);
        double ty = 4.0 * t - 2.0;

        double[] coefficients =
        {
            -1.3026537197817094, 6.4196979235649026e-1, 1.9476473204185836e-2,
            -9.561514786808631e-3, -9.46595344482036e-4, 3.66839497852761e-4,
            4.2523324806907e-5, -2.0278578112534e-5, -1.624290004647e-6,
            1.303655835580e-6, 1.5626441722e-8, -8.5238095915e-8,
            6.529054439e-9, 5.059343495e-9, -9.91364156e-10,
            -2.27365122e-10, 9.6467911e-11, 2.394038e-12,
            -6.886027e-12, 8.94487e-13, 3.13092e-13,
            -1.12708e-13, 3.81e-16, 7.106e-15
        };

        double d = 0.0;
        double dd = 0.0;
        for (int j = coefficients.Length - 1; j > 0; j--)
        {
            double tmp = d;
            d = ty * d - dd + coefficients[j];
            dd = tmp;
        }

        double result = t * Math.Exp(-z * z + 0.5 * (coefficients[0] + ty * d) - dd);
        return x >= 0.0 ? result : 2.0 - result;
    }
}
