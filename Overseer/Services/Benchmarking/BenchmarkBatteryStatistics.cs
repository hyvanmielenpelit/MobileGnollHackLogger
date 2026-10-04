namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json.Serialization;
using MobileGnollHackLogger.Data;

/// <summary>A battery member left out of every statistic, and why (M4).</summary>
public sealed record BenchmarkBatteryExcludedMember(int SuiteIndex, int Round, long RunId, string Reason);

/// <summary>
/// One suite of a battery run, as the analysis service hands it to
/// <see cref="BenchmarkBatteryStatistics.Compute"/>.
/// </summary>
/// <param name="SuiteIndex">0-based position in the battery definition.</param>
/// <param name="Statistics">The group statistics over the suite's usable members; null when it has none.</param>
/// <param name="Mass">Exam question count and difficulty mass, from the suite's exam (M2).</param>
/// <param name="RoundByRunId">The round of every usable member run.</param>
/// <param name="ModelTimesMs">Model-attributable time of every answer pooled into the speed percentiles.</param>
/// <param name="TtftMs">Time to first token of every answer that reported one.</param>
/// <param name="UsableMemberCount">Usable members of the suite (M4).</param>
/// <param name="ExpectedQuestionCount">The members' <c>TotalQuestionCount</c>, for the exam check of M2.</param>
/// <param name="Excluded">The suite's members left out, with their reasons.</param>
/// <param name="PanelVerificationClearedLifts">
/// Per usable panel member run, its panel verification-cleared Accuracy sensitivity
/// (<see cref="BenchmarkPanelSensitivity"/>) minus its published Intelligence Index: zero when nothing
/// is lifted. Null or empty when no member is a panel run.
/// </param>
public sealed record BenchmarkBatterySuiteInput(
    int SuiteIndex,
    BenchmarkGroupStatisticsResult? Statistics,
    BenchmarkBatterySuiteMass Mass,
    IReadOnlyDictionary<long, int> RoundByRunId,
    IReadOnlyList<double> ModelTimesMs,
    IReadOnlyList<double> TtftMs,
    int UsableMemberCount,
    int ExpectedQuestionCount,
    IReadOnlyList<BenchmarkBatteryExcludedMember> Excluded,
    IReadOnlyList<double>? PanelVerificationClearedLifts = null);

/// <summary>
/// Degradation flags derived from the battery-wide comparability verdict (M8): a speed- or
/// cost-affecting key that differs between suites.
/// </summary>
public sealed record BenchmarkBatteryStatisticsOptions
{
    public bool SpeedDegraded { get; init; }
    public string? SpeedDegradedReason { get; init; }
    public bool CostDegraded { get; init; }
    public string? CostDegradedReason { get; init; }
}

/// <summary>Where the reproducibility component of the Overall Index interval came from (M3).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BenchmarkBatteryReproducibilitySource
{
    /// <summary>Fewer than three complete rounds, and no per-suite fallback available.</summary>
    NotAvailable = 0,

    /// <summary>The standard deviation of the per-round composites.</summary>
    Rounds = 1,

    /// <summary>Ragged rounds: the suites' own reproducibility standard errors in quadrature.</summary>
    PerSuiteFallback = 2
}

/// <summary>How the stratified sign-flip p-value was obtained (M7).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BenchmarkBatteryRandomizationMethod
{
    NotComputed = 0,

    /// <summary>Every sign assignment enumerated.</summary>
    Exact = 1,

    /// <summary>Seeded Monte Carlo resamples with the Phipson–Smyth p.</summary>
    MonteCarlo = 2
}

/// <summary>One complete battery round's composite index.</summary>
public sealed record BenchmarkBatteryRoundIndex
{
    public int Round { get; init; }
    public double Index { get; init; }
}

/// <summary>One member run's index within its suite, computed from the suite's item rows.</summary>
public sealed record BenchmarkBatteryRunIndex
{
    public long RunId { get; init; }

    /// <summary>Null when the run has no recorded round.</summary>
    public int? Round { get; init; }

    public double Index { get; init; }
}

/// <summary>The Overall Intelligence Index and its two uncertainty components (M2, M3).</summary>
public sealed record BenchmarkBatteryOverallIndex
{
    /// <summary><c>Σ w_s · I_s</c>.</summary>
    public double PointEstimate { get; init; }

    /// <summary><c>√(Σ w_s² · SE_item,s²)</c>. Null when any suite withholds its own.</summary>
    public double? ItemSamplingStandardError { get; init; }

    /// <summary>Welch–Satterthwaite ν over the suites' scored item counts, unfloored.</summary>
    public double? EffectiveDegreesOfFreedom { get; init; }

    /// <summary><c>t(⌊ν⌋)</c>, ν never below 1.</summary>
    public double? ItemSamplingCriticalValue { get; init; }

    public double? ItemSamplingHalfWidth { get; init; }

    /// <summary>Suites whose item-sampling standard error is null (fewer than three scored items).</summary>
    public IReadOnlyList<int> ItemSamplingWithheldBySuiteIndex { get; init; } = Array.Empty<int>();

    public double? ReproducibilityStandardError { get; init; }

    /// <summary><c>R − 1</c> from rounds; Satterthwaite over <c>R_s − 1</c> for the per-suite fallback.</summary>
    public double? ReproducibilityDegreesOfFreedom { get; init; }

    public double? ReproducibilityCriticalValue { get; init; }

    public double? ReproducibilityHalfWidth { get; init; }

    public BenchmarkBatteryReproducibilitySource ReproducibilitySource { get; init; }

    /// <summary>Complete rounds, every suite having an index in each. Zero when the rounds are ragged.</summary>
    public int RoundCount { get; init; }

    /// <summary>The per-round composites, in round order. Empty when the rounds are ragged.</summary>
    public IReadOnlyList<BenchmarkBatteryRoundIndex> PerRoundIndices { get; init; } = Array.Empty<BenchmarkBatteryRoundIndex>();

    /// <summary>Both half-widths in quadrature, or the one available. Never clamped.</summary>
    public double? CombinedHalfWidth { get; init; }

    /// <summary>Bounds clamped to [0, 100].</summary>
    public double? CombinedLower { get; init; }

    /// <inheritdoc cref="CombinedLower"/>
    public double? CombinedUpper { get; init; }

    public bool CombinedIntervalTruncated { get; init; }
}

/// <summary>One suite's row of the battery profile (M5).</summary>
public sealed record BenchmarkBatterySuiteProfile
{
    public int SuiteIndex { get; init; }
    public long SuiteId { get; init; }
    public string SuiteName { get; init; } = string.Empty;

    /// <summary>The suite has at least one usable member with a scored item.</summary>
    public bool Complete { get; init; }

    /// <summary>The declared weight <c>w_s</c>. Null when the weights could not be computed.</summary>
    public double? Weight { get; init; }

    /// <summary>The count weight <c>v_s</c> of M5 and M6.</summary>
    public double? CountWeight { get; init; }

    public int ExamItemCount { get; init; }
    public double DifficultyMass { get; init; }
    public int ExpectedQuestionCount { get; init; }

    /// <summary>The exam holds fewer questions than the members' <c>TotalQuestionCount</c>.</summary>
    public bool ExamIncomplete { get; init; }

    /// <summary>Items with at least one scored answer.</summary>
    public int ScoredItemCount { get; init; }

    public int UsableMemberCount { get; init; }

    /// <summary><c>I_s</c>: the group's unrounded multi-run index. Null when the suite is not complete.</summary>
    public double? Index { get; init; }

    /// <summary><c>w_s · I_s</c>.</summary>
    public double? Contribution { get; init; }

    /// <summary>The suite's own interval, on the group layer's 1.96 for item sampling.</summary>
    public double? ItemSamplingStandardError { get; init; }

    public double? ReproducibilityStandardError { get; init; }
    public double? CombinedHalfWidth { get; init; }
    public double? CombinedLower { get; init; }
    public double? CombinedUpper { get; init; }
    public bool CombinedIntervalTruncated { get; init; }
    public bool IdentityHolds { get; init; }

    /// <summary>Per-run indices from the item rows, aligned to their runs and rounds.</summary>
    public IReadOnlyList<BenchmarkBatteryRunIndex> RunIndices { get; init; } = Array.Empty<BenchmarkBatteryRunIndex>();

    public double? MeanSpeedIndex { get; init; }

    /// <summary>Mean of the suite's item critical-error rates.</summary>
    public double? CriticalErrorRate { get; init; }

    public double? TotalCost { get; init; }
    public double? MeanCostPerRun { get; init; }

    /// <summary>
    /// Advisory: <see cref="Index"/> plus the mean lift of the suite's usable panel member runs under
    /// the panel verification-cleared Accuracy sensitivity (<see cref="BenchmarkBatterySuiteInput.PanelVerificationClearedLifts"/>).
    /// Null when the suite is not complete or no member is a panel run, and on a result stored before
    /// the figure existed.
    /// </summary>
    public double? PanelVerificationClearedIndex { get; init; }

    /// <summary>
    /// The suite's full group statistics, kept so a persisted result can be compared with another
    /// (<see cref="BenchmarkBatteryStatistics.Compare"/>) without reloading its runs.
    /// </summary>
    public BenchmarkGroupStatisticsResult? Statistics { get; init; }
}

/// <summary>The Overall Index under one weighting scheme (M5).</summary>
public sealed record BenchmarkBatterySchemeIndex
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BenchmarkBatteryWeightingScheme Scheme { get; init; }

    /// <summary>The battery's own scheme; every other row is a sensitivity figure.</summary>
    public bool Declared { get; init; }

    public IReadOnlyList<double> Weights { get; init; } = Array.Empty<double>();
    public double Index { get; init; }
}

/// <summary>The Overall Index with one suite omitted and the remaining declared weights renormalized.</summary>
public sealed record BenchmarkBatteryLeaveOneOut
{
    public int SuiteIndex { get; init; }
    public string SuiteName { get; init; } = string.Empty;
    public double? Index { get; init; }

    /// <summary><see cref="Index"/> minus the Overall Index.</summary>
    public double? Change { get; init; }
}

/// <summary>One scoring dimension as <c>Σ v_s · dim_s</c>.</summary>
public sealed record BenchmarkBatteryDimension
{
    public string Dimension { get; init; } = string.Empty;

    /// <summary>Null when any suite's mean is null.</summary>
    public double? Mean { get; init; }

    public IReadOnlyList<int> WithheldBySuiteIndex { get; init; } = Array.Empty<int>();
}

/// <summary>Speed across the battery (M6).</summary>
public sealed record BenchmarkBatterySpeedStatistics
{
    /// <summary><c>Σ v_s · MeanSpeedIndex_s</c>. Null when any suite's is null.</summary>
    public double? OverallSpeedIndex { get; init; }

    public IReadOnlyList<int> SpeedIndexWithheldBySuiteIndex { get; init; } = Array.Empty<int>();

    public int PooledAnswerCount { get; init; }
    public double? ModelTimeP50Ms { get; init; }
    public double? ModelTimeP90Ms { get; init; }
    public double? ModelTimeMaxMs { get; init; }
    public double? ModelTimeMeanMs { get; init; }

    /// <summary>Sum of candidate answer time over every pooled answer.</summary>
    public double TotalModelTimeMs { get; init; }

    public int TtftAnswerCount { get; init; }
    public double? TtftP50Ms { get; init; }
    public double? TtftP90Ms { get; init; }
    public double? TtftMaxMs { get; init; }

    public bool Degraded { get; init; }
    public string? DegradedReason { get; init; }
}

/// <summary>
/// Cost across the battery (M6). Every figure is null when any suite's cost is unknown.
/// </summary>
public sealed record BenchmarkBatteryCostStatistics
{
    public bool Available { get; init; }
    public IReadOnlyList<int> WithheldBySuiteIndex { get; init; } = Array.Empty<int>();
    public string? WithheldReason { get; init; }

    /// <summary>Σ of the suites' <c>TotalCost</c>: every usable member run.</summary>
    public double? TotalCost { get; init; }

    /// <summary>One battery pass: Σ of the suites' <c>MeanCostPerRun</c>.</summary>
    public double? PassCost { get; init; }

    public IReadOnlyDictionary<string, double>? TotalCostByRole { get; init; }
    public IReadOnlyDictionary<string, double>? PassCostByRole { get; init; }

    /// <summary>Answer rows of the costed runs.</summary>
    public int? AnswerRowCount { get; init; }

    /// <summary><see cref="TotalCost"/> ÷ <see cref="AnswerRowCount"/>.</summary>
    public double? CostPerQuestion { get; init; }

    /// <summary><see cref="PassCost"/> ÷ the Overall Index; null at a non-positive index.</summary>
    public double? CostPerIndexPoint { get; init; }

    public bool Degraded { get; init; }
    public string? DegradedReason { get; init; }
}

/// <summary>Token, tool and claim totals summed over the suites.</summary>
public sealed record BenchmarkBatteryUsageStatistics
{
    public long TotalInputTokens { get; init; }
    public long TotalOutputTokens { get; init; }
    public long TotalCacheReadTokens { get; init; }
    public long TotalAssessmentInputTokens { get; init; }
    public long TotalAssessmentOutputTokens { get; init; }
    public long TotalClaimVerificationInputTokens { get; init; }
    public long TotalClaimVerificationOutputTokens { get; init; }
    public int TotalToolCalls { get; init; }

    /// <summary>Null when no suite reported model calls.</summary>
    public int? TotalModelCalls { get; init; }

    public IReadOnlyDictionary<string, int> ToolCallsByFamily { get; init; } = new Dictionary<string, int>();
    public int ClaimsSupported { get; init; }
    public int ClaimsRefuted { get; init; }
    public int ClaimsIndeterminate { get; init; }
    public int ClaimsChecked { get; init; }
}

/// <summary>The whole battery analysis (M2–M6). Pure arithmetic; every figure is reproducible.</summary>
public sealed record BenchmarkBatteryStatisticsResult
{
    public int MethodVersion { get; init; } = BenchmarkBatteryStatistics.CurrentMethodVersion;

    /// <summary>Every suite has a usable member and the weights resolved, so the headline exists (M4).</summary>
    public bool Complete { get; init; }

    public int CompletedSuiteCount { get; init; }
    public int SuiteCount { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BenchmarkBatteryWeightingScheme Scheme { get; init; }

    /// <summary>The declared weights actually used, in suite order. Empty when they could not be computed.</summary>
    public IReadOnlyList<double> Weights { get; init; } = Array.Empty<double>();

    /// <summary>The count weights <c>v_s</c> (M5, M6), in suite order.</summary>
    public IReadOnlyList<double> CountWeights { get; init; } = Array.Empty<double>();

    public IReadOnlyList<BenchmarkBatterySuiteMass> SuiteMasses { get; init; } = Array.Empty<BenchmarkBatterySuiteMass>();

    public IReadOnlyList<BenchmarkBatteryExcludedMember> ExcludedMembers { get; init; } = Array.Empty<BenchmarkBatteryExcludedMember>();

    /// <summary>
    /// Every suite's group identity holds and every exam question of every suite has an item row,
    /// so the default-scheme Overall Index equals the pooled difficulty-weighted index exactly (M2).
    /// </summary>
    public bool PooledIdentityHolds { get; init; }

    /// <summary>Null when the battery is incomplete.</summary>
    public BenchmarkBatteryOverallIndex? OverallIndex { get; init; }

    /// <summary>One profile row per suite, in suite order, present whether or not the battery is complete.</summary>
    public IReadOnlyList<BenchmarkBatterySuiteProfile> Suites { get; init; } = Array.Empty<BenchmarkBatterySuiteProfile>();

    /// <summary>Sample standard deviation of the suite indices: profile unevenness.</summary>
    public double? BetweenSuiteStandardDeviation { get; init; }

    public double? BetweenSuiteRange { get; init; }

    /// <summary>The declared scheme first, then every other automatic scheme.</summary>
    public IReadOnlyList<BenchmarkBatterySchemeIndex> WeightingSensitivity { get; init; } = Array.Empty<BenchmarkBatterySchemeIndex>();

    /// <summary>
    /// Advisory: <c>Σ w_s · I_s</c> under the declared weights with each suite's
    /// <see cref="BenchmarkBatterySuiteProfile.PanelVerificationClearedIndex"/> in place of its index,
    /// and the published index for a suite without one. Null when the battery is incomplete or no
    /// member is a panel run, and on a result stored before the figure existed.
    /// </summary>
    public double? PanelVerificationClearedOverall { get; init; }

    public IReadOnlyList<BenchmarkBatteryLeaveOneOut> LeaveOneSuiteOut { get; init; } = Array.Empty<BenchmarkBatteryLeaveOneOut>();

    /// <summary>Accuracy, Completeness, Conciseness, Readability. Empty when the battery is incomplete.</summary>
    public IReadOnlyList<BenchmarkBatteryDimension> Dimensions { get; init; } = Array.Empty<BenchmarkBatteryDimension>();

    /// <summary><c>Σ v_s · rate_s</c>. Null when the battery is incomplete.</summary>
    public double? CriticalErrorRate { get; init; }

    /// <summary>Null when the battery is incomplete.</summary>
    public BenchmarkBatterySpeedStatistics? Speed { get; init; }

    /// <summary>Null when the battery is incomplete.</summary>
    public BenchmarkBatteryCostStatistics? Cost { get; init; }

    /// <summary>Sums over the suites that have statistics. Null when none recorded usage.</summary>
    public BenchmarkBatteryUsageStatistics? Usage { get; init; }

    public IReadOnlyList<string> Caveats { get; init; } = Array.Empty<string>();
}

/// <summary>Knobs for <see cref="BenchmarkBatteryStatistics.Compare"/>.</summary>
public sealed record BenchmarkBatteryCompareOptions
{
    public int MonteCarloResamples { get; init; } = BenchmarkBatteryStatistics.DefaultMonteCarloResamples;
    public int Seed { get; init; } = BenchmarkBatteryStatistics.DefaultSeed;

    /// <summary>At or below this many paired items in total, the sign-flip p is exact.</summary>
    public int MaxExactPairedItems { get; init; } = BenchmarkBatteryStatistics.MaxExactPairedItems;

    /// <summary>For the per-suite exploratory item tests of the existing group comparison.</summary>
    public double FalseDiscoveryRate { get; init; } = BenchmarkGroupStatistics.DefaultFalseDiscoveryRate;
}

/// <summary>One suite's part of a battery comparison.</summary>
public sealed record BenchmarkBatterySuiteComparison
{
    public int SuiteIndex { get; init; }
    public string SuiteName { get; init; } = string.Empty;

    /// <summary>The baseline's declared weight <c>w_s</c>.</summary>
    public double? Weight { get; init; }

    /// <summary>Items present on both sides under equal item revisions.</summary>
    public int PairedItemCount { get; init; }

    /// <summary><c>ΔI_s = Σ d_q Δ_q / Σ d_q</c>. Null without a paired item.</summary>
    public double? WeightedDifference { get; init; }

    /// <summary>The unrounded weighted standard error over <c>(Δ_q, d_q)</c>. Null below three pairs.</summary>
    public double? WeightedDifferenceStandardError { get; init; }

    public double? WilcoxonPValue { get; init; }

    /// <summary>Holm-adjusted across the suites with a Wilcoxon p.</summary>
    public double? HolmAdjustedPValue { get; init; }

    /// <summary>The existing per-suite group comparison: the secondary figures.</summary>
    public BenchmarkGroupComparison? Comparison { get; init; }

    public string? Note { get; init; }
}

/// <summary>A paired comparison of two battery results (M7). Differences are treatment minus baseline.</summary>
public sealed record BenchmarkBatteryComparison
{
    public int MethodVersion { get; init; } = BenchmarkBatteryStatistics.CurrentMethodVersion;

    public double? BaselineOverallIndex { get; init; }
    public double? TreatmentOverallIndex { get; init; }

    public IReadOnlyList<BenchmarkBatterySuiteComparison> Suites { get; init; } = Array.Empty<BenchmarkBatterySuiteComparison>();

    public int PairedItemCount { get; init; }

    /// <summary><c>D = Σ w_s · ΔI_s</c>. Null when a suite has no paired item.</summary>
    public double? CompositeDifference { get; init; }

    public IReadOnlyList<int> CompositeWithheldBySuiteIndex { get; init; } = Array.Empty<int>();

    /// <summary><c>√(Σ w_s² · SE_Δ,s²)</c>. Null when a suite has fewer than three pairs.</summary>
    public double? CompositeStandardError { get; init; }

    public IReadOnlyList<int> StandardErrorWithheldBySuiteIndex { get; init; } = Array.Empty<int>();

    /// <summary>Satterthwaite ν over <c>m_s − 1</c>, unfloored.</summary>
    public double? CompositeDegreesOfFreedom { get; init; }

    public double? CompositeCriticalValue { get; init; }
    public double? CompositeConfidenceHalfWidth { get; init; }
    public double? CompositeConfidenceLower { get; init; }
    public double? CompositeConfidenceUpper { get; init; }

    /// <summary>Two-sided p of the stratified paired sign-flip test on <c>D</c>.</summary>
    public double? RandomizationPValue { get; init; }

    public BenchmarkBatteryRandomizationMethod RandomizationMethod { get; init; }

    public int? MonteCarloResamples { get; init; }
    public double? MonteCarloStandardError { get; init; }
    public int? Seed { get; init; }

    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

/// <summary>
/// The battery composite: a stratified, declared-weight mean of per-suite multi-run indices, its
/// uncertainty, diagnostics, and a paired comparison of two results. Implements M2–M7 of the
/// multi-suite method (<c>docs/overseer/ai-benchmark-multi-suite.md</c>). Pure functions: no I/O.
/// </summary>
public static class BenchmarkBatteryStatistics
{
    public const int CurrentMethodVersion = 1;

    public const int DefaultMonteCarloResamples = 100_000;

    public const int DefaultSeed = 20261001;

    /// <summary>At or below this many paired items in total, the sign-flip test enumerates every assignment.</summary>
    public const int MaxExactPairedItems = 20;

    /// <summary>Upper bound on exact enumeration whatever the options say: 2^30 assignments.</summary>
    private const int MaxEnumerablePairedItems = 30;

    /// <summary>A resampled statistic counts as at least as extreme when <c>|D*| ≥ |D| − TieTolerance</c>.</summary>
    public const double TieTolerance = 1e-12;

    private const double IdentityTolerance = 1e-9;

    public const string CriticalValueCaveat =
        "The Overall Index uses Student's t on Welch-Satterthwaite degrees of freedom for its item-sampling "
        + "component, while each suite's own interval in the profile uses the normal 1.96, so the headline "
        + "interval is the wider of the two at the same item count.";

    public const string NoReproducibilityCaveat =
        "Fewer than three complete battery rounds: no reproducibility figure is reported, and the interval "
        + "covers item sampling only.";

    public const string PerSuiteFallbackCaveat =
        "The rounds are ragged, so the reproducibility component combines the suites' own reproducibility "
        + "standard errors in quadrature, which assumes run-to-run variation is independent between suites.";

    public const string PooledIdentityCaveat =
        "Some exam question has no scored answer, so the Overall Index equals the pooled difficulty-weighted "
        + "index over every question only approximately.";

    private static readonly string[] DimensionNames = { "Accuracy", "Completeness", "Conciseness", "Readability" };

    private static readonly BenchmarkBatteryWeightingScheme[] AutomaticSchemes =
    {
        BenchmarkBatteryWeightingScheme.DifficultyMass,
        BenchmarkBatteryWeightingScheme.ItemCount,
        BenchmarkBatteryWeightingScheme.Equal
    };

    // --- Composite ------------------------------------------------------------------------------

    /// <summary>
    /// Computes the battery analysis. <paramref name="suites"/> must hold exactly one input per suite
    /// of <paramref name="definition"/>, matched on <c>SuiteIndex</c>.
    /// </summary>
    public static BenchmarkBatteryStatisticsResult Compute(
        BenchmarkBatteryDefinition definition,
        IReadOnlyList<BenchmarkBatterySuiteInput> suites,
        BenchmarkBatteryStatisticsOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(suites);
        var cfg = options ?? new BenchmarkBatteryStatisticsOptions();

        var defs = definition.Suites.OrderBy(s => s.Index).ToList();
        int k = defs.Count;

        var byIndex = new Dictionary<int, BenchmarkBatterySuiteInput>();
        foreach (var input in suites.Where(s => s != null))
        {
            if (!byIndex.TryAdd(input.SuiteIndex, input))
            {
                throw new ArgumentException($"Two inputs for suite index {input.SuiteIndex}.", nameof(suites));
            }
        }

        var inputs = defs
            .Select(d => byIndex.TryGetValue(d.Index, out var input)
                ? input
                : throw new ArgumentException($"No input for suite index {d.Index}.", nameof(suites)))
            .ToList();

        var masses = inputs.Select(i => i.Mass).ToList();
        var customWeights = defs.Select(d => d.CustomWeight).ToList();

        var weights = TryWeights(() => BenchmarkBatteryDefinition.Weights(definition.Scheme, masses, customWeights), k);
        var countWeights = TryWeights(() => BenchmarkBatteryDefinition.CountWeights(definition.Scheme, masses, customWeights), k);

        var caveats = new List<string>();

        bool[] suiteComplete = inputs.Select(IsSuiteComplete).ToArray();
        int completed = suiteComplete.Count(c => c);
        bool weightsResolved = weights != null && countWeights != null;
        bool complete = k > 0 && completed == k && weightsResolved;

        if (completed < k)
        {
            caveats.Add($"Incomplete ({completed} of {k} suites): no Overall Index is reported.");
        }
        else if (!weightsResolved)
        {
            caveats.Add("The suite weights could not be computed, so no Overall Index is reported.");
        }

        for (int i = 0; i < k; i++)
        {
            if (inputs[i].Statistics != null && inputs[i].Mass.ItemCount < inputs[i].ExpectedQuestionCount)
            {
                caveats.Add($"Exam incomplete for suite '{defs[i].SuiteName}': {inputs[i].Mass.ItemCount} of "
                    + $"{inputs[i].ExpectedQuestionCount} questions.");
            }
        }

        var profiles = new List<BenchmarkBatterySuiteProfile>(k);
        for (int i = 0; i < k; i++)
        {
            profiles.Add(BuildProfile(defs[i], inputs[i], suiteComplete[i], weights?[i], countWeights?[i]));
        }

        bool pooledIdentityHolds = complete && inputs.All(PooledIdentityHoldsFor);
        if (complete && !pooledIdentityHolds)
        {
            caveats.Add(PooledIdentityCaveat);
        }

        BenchmarkBatteryOverallIndex? overall = null;
        double? betweenSd = null;
        double? betweenRange = null;
        var sensitivity = new List<BenchmarkBatterySchemeIndex>();
        var leaveOneOut = new List<BenchmarkBatteryLeaveOneOut>();
        var dimensions = new List<BenchmarkBatteryDimension>();
        double? panelVerificationClearedOverall = null;
        double? criticalErrorRate = null;
        BenchmarkBatterySpeedStatistics? speed = null;
        BenchmarkBatteryCostStatistics? cost = null;

        if (complete)
        {
            var w = weights!;
            var v = countWeights!;
            var stats = inputs.Select(i => i.Statistics!).ToList();
            var suiteIndices = stats.Select(s => s.Index.PointEstimate).ToList();
            double point = WeightedSum(w, suiteIndices);

            if (profiles.Any(p => p.PanelVerificationClearedIndex.HasValue))
            {
                panelVerificationClearedOverall = WeightedSum(
                    w,
                    Enumerable.Range(0, k).Select(s => profiles[s].PanelVerificationClearedIndex ?? suiteIndices[s]).ToList());
            }

            overall = ComputeOverall(defs, inputs, stats, w, point, caveats);
            caveats.Add(CriticalValueCaveat);

            betweenSd = BenchmarkGroupStatistics.SampleStandardDeviation(suiteIndices);
            betweenRange = suiteIndices.Max() - suiteIndices.Min();

            sensitivity.Add(new BenchmarkBatterySchemeIndex
            {
                Scheme = definition.Scheme,
                Declared = true,
                Weights = w,
                Index = point
            });
            foreach (var scheme in AutomaticSchemes.Where(s => s != definition.Scheme))
            {
                var alternative = TryWeights(() => BenchmarkBatteryDefinition.Weights(scheme, masses, null), k);
                if (alternative == null) continue;
                sensitivity.Add(new BenchmarkBatterySchemeIndex
                {
                    Scheme = scheme,
                    Weights = alternative,
                    Index = WeightedSum(alternative, suiteIndices)
                });
            }

            for (int s = 0; s < k; s++)
            {
                double rest = 0.0;
                double weighted = 0.0;
                for (int j = 0; j < k; j++)
                {
                    if (j == s) continue;
                    rest += w[j];
                    weighted += w[j] * suiteIndices[j];
                }

                double? index = rest > 0.0 ? weighted / rest : null;
                leaveOneOut.Add(new BenchmarkBatteryLeaveOneOut
                {
                    SuiteIndex = defs[s].Index,
                    SuiteName = defs[s].SuiteName,
                    Index = index,
                    Change = index.HasValue ? index.Value - point : null
                });
            }

            foreach (string name in DimensionNames)
            {
                var withheld = new List<int>();
                double sum = 0.0;
                for (int s = 0; s < k; s++)
                {
                    double? mean = stats[s].Dimensions.FirstOrDefault(d => d.Dimension == name)?.Mean;
                    if (mean.HasValue) sum += v[s] * mean.Value;
                    else withheld.Add(defs[s].Index);
                }

                dimensions.Add(new BenchmarkBatteryDimension
                {
                    Dimension = name,
                    Mean = withheld.Count == 0 ? sum : null,
                    WithheldBySuiteIndex = withheld
                });
            }

            criticalErrorRate = WeightedSum(v, stats.Select(s => s.Items.Average(i => i.CriticalErrorRate)).ToList());

            speed = ComputeSpeed(defs, inputs, stats, v, cfg, caveats);
            cost = ComputeCost(defs, inputs, stats, point, cfg, caveats);
        }

        return new BenchmarkBatteryStatisticsResult
        {
            Complete = complete,
            CompletedSuiteCount = completed,
            SuiteCount = k,
            Scheme = definition.Scheme,
            Weights = weights ?? Array.Empty<double>(),
            CountWeights = countWeights ?? Array.Empty<double>(),
            SuiteMasses = masses,
            ExcludedMembers = inputs.SelectMany(i => i.Excluded ?? Array.Empty<BenchmarkBatteryExcludedMember>()).ToList(),
            PooledIdentityHolds = pooledIdentityHolds,
            OverallIndex = overall,
            Suites = profiles,
            BetweenSuiteStandardDeviation = betweenSd,
            BetweenSuiteRange = betweenRange,
            WeightingSensitivity = sensitivity,
            PanelVerificationClearedOverall = panelVerificationClearedOverall,
            LeaveOneSuiteOut = leaveOneOut,
            Dimensions = dimensions,
            CriticalErrorRate = criticalErrorRate,
            Speed = speed,
            Cost = cost,
            Usage = ComputeUsage(inputs),
            Caveats = caveats
        };
    }

    /// <summary>
    /// Each member run's index within its suite, from the item rows: for the run,
    /// <c>Σ Weight · score / Σ Weight</c> over the items whose <c>RunIds</c> contain it. A run with
    /// no scored item is absent.
    /// </summary>
    public static IReadOnlyDictionary<long, double> PerRunIndexByRunId(BenchmarkGroupStatisticsResult statistics)
    {
        ArgumentNullException.ThrowIfNull(statistics);

        var weighted = new Dictionary<long, double>();
        var weights = new Dictionary<long, double>();
        foreach (var item in statistics.Items)
        {
            for (int i = 0; i < item.Scores.Count && i < item.RunIds.Count; i++)
            {
                long runId = item.RunIds[i];
                weighted[runId] = weighted.GetValueOrDefault(runId) + item.Weight * item.Scores[i];
                weights[runId] = weights.GetValueOrDefault(runId) + item.Weight;
            }
        }

        return weights
            .Where(kv => kv.Value > 0.0)
            .ToDictionary(kv => kv.Key, kv => weighted[kv.Key] / kv.Value);
    }

    private static bool IsSuiteComplete(BenchmarkBatterySuiteInput input)
        => input.Statistics != null
           && input.Statistics.Items.Count > 0
           && input.Statistics.Index.PerRunIndices.Count > 0;

    private static bool PooledIdentityHoldsFor(BenchmarkBatterySuiteInput input)
    {
        var stats = input.Statistics!;
        return stats.Index.IdentityHolds
               && stats.UnansweredItemCount == 0
               && stats.ItemCount == input.Mass.ItemCount
               && input.Mass.ItemCount >= input.ExpectedQuestionCount;
    }

    private static BenchmarkBatterySuiteProfile BuildProfile(
        BenchmarkBatteryDefinitionSuite def,
        BenchmarkBatterySuiteInput input,
        bool complete,
        double? weight,
        double? countWeight)
    {
        var stats = input.Statistics;
        double? index = complete ? stats!.Index.PointEstimate : null;

        var runIndices = new List<BenchmarkBatteryRunIndex>();
        if (stats != null)
        {
            var perRun = PerRunIndexByRunId(stats);
            foreach (long runId in stats.RunIds)
            {
                if (!perRun.TryGetValue(runId, out double runIndex)) continue;
                runIndices.Add(new BenchmarkBatteryRunIndex
                {
                    RunId = runId,
                    Round = input.RoundByRunId != null && input.RoundByRunId.TryGetValue(runId, out int round) ? round : null,
                    Index = runIndex
                });
            }
        }

        double? panelVerificationCleared = index.HasValue && input.PanelVerificationClearedLifts is { Count: > 0 } lifts
            ? index.Value + lifts.Average()
            : null;

        return new BenchmarkBatterySuiteProfile
        {
            SuiteIndex = def.Index,
            SuiteId = def.SuiteId,
            SuiteName = def.SuiteName,
            Complete = complete,
            Weight = weight,
            CountWeight = countWeight,
            ExamItemCount = input.Mass.ItemCount,
            DifficultyMass = input.Mass.DifficultyMass,
            ExpectedQuestionCount = input.ExpectedQuestionCount,
            ExamIncomplete = input.Mass.ItemCount < input.ExpectedQuestionCount,
            ScoredItemCount = stats?.ItemCount ?? 0,
            UsableMemberCount = input.UsableMemberCount,
            Index = index,
            Contribution = index.HasValue && weight.HasValue ? weight.Value * index.Value : null,
            ItemSamplingStandardError = complete ? stats!.Index.ItemSamplingStandardError : null,
            ReproducibilityStandardError = complete ? stats!.Index.ReproducibilityStandardError : null,
            CombinedHalfWidth = complete ? stats!.Index.CombinedHalfWidth : null,
            CombinedLower = complete ? stats!.Index.CombinedLower : null,
            CombinedUpper = complete ? stats!.Index.CombinedUpper : null,
            CombinedIntervalTruncated = complete && stats!.Index.CombinedIntervalTruncated,
            IdentityHolds = stats?.Index.IdentityHolds ?? false,
            RunIndices = runIndices,
            MeanSpeedIndex = stats?.Speed.MeanSpeedIndex,
            CriticalErrorRate = stats != null && stats.Items.Count > 0 ? stats.Items.Average(i => i.CriticalErrorRate) : null,
            TotalCost = stats?.Cost?.TotalCost,
            MeanCostPerRun = stats?.Cost?.MeanCostPerRun,
            PanelVerificationClearedIndex = panelVerificationCleared,
            Statistics = stats
        };
    }

    private static BenchmarkBatteryOverallIndex ComputeOverall(
        IReadOnlyList<BenchmarkBatteryDefinitionSuite> defs,
        IReadOnlyList<BenchmarkBatterySuiteInput> inputs,
        IReadOnlyList<BenchmarkGroupStatisticsResult> stats,
        IReadOnlyList<double> w,
        double point,
        List<string> caveats)
    {
        int k = stats.Count;

        // Item sampling: suites are independent strata.
        var itemWithheld = new List<int>();
        for (int s = 0; s < k; s++)
        {
            if (!stats[s].Index.ItemSamplingStandardError.HasValue)
            {
                itemWithheld.Add(defs[s].Index);
                caveats.Add($"Item-sampling standard error withheld by suite '{defs[s].SuiteName}' "
                    + "(fewer than three scored items).");
            }
        }

        double? itemSe = null;
        double? itemDf = null;
        double? itemCrit = null;
        double? itemHalf = null;
        if (itemWithheld.Count == 0)
        {
            var se = stats.Select(s => s.Index.ItemSamplingStandardError!.Value).ToList();
            itemSe = Math.Sqrt(Enumerable.Range(0, k).Sum(s => w[s] * w[s] * se[s] * se[s]));
            itemDf = SatterthwaiteDegreesOfFreedom(w, se, stats.Select(s => s.ItemCount - 1).ToList());
            itemCrit = BenchmarkGroupStatistics.StudentTCritical95(FloorDegreesOfFreedom(itemDf.Value));
            itemHalf = itemSe.Value > 0.0 ? itemCrit.Value * itemSe.Value : 0.0;
        }

        // Reproducibility: per-round composites when every suite has an index in the same rounds.
        var roundMaps = new List<Dictionary<int, double>>(k);
        bool aligned = true;
        for (int s = 0; s < k; s++)
        {
            var perRun = PerRunIndexByRunId(stats[s]);
            var map = new Dictionary<int, double>();
            foreach (long runId in stats[s].RunIds)
            {
                if (!perRun.TryGetValue(runId, out double runIndex)
                    || inputs[s].RoundByRunId == null
                    || !inputs[s].RoundByRunId.TryGetValue(runId, out int round)
                    || !map.TryAdd(round, runIndex))
                {
                    aligned = false;
                }
            }

            roundMaps.Add(map);
        }

        if (aligned)
        {
            var firstRounds = roundMaps[0].Keys.OrderBy(r => r).ToList();
            aligned = roundMaps.All(m => m.Count == firstRounds.Count && firstRounds.All(m.ContainsKey));
        }

        var perRound = new List<BenchmarkBatteryRoundIndex>();
        if (aligned)
        {
            foreach (int round in roundMaps[0].Keys.OrderBy(r => r))
            {
                double composite = 0.0;
                for (int s = 0; s < k; s++)
                {
                    composite += w[s] * roundMaps[s][round];
                }

                perRound.Add(new BenchmarkBatteryRoundIndex { Round = round, Index = composite });
            }
        }

        var source = BenchmarkBatteryReproducibilitySource.NotAvailable;
        double? reproSe = null;
        double? reproDf = null;
        double? reproCrit = null;
        double? reproHalf = null;

        if (aligned && perRound.Count >= BenchmarkGroupStatistics.MinRunsForReproducibility)
        {
            int r = perRound.Count;
            double sd = BenchmarkGroupStatistics.SampleStandardDeviation(perRound.Select(p => p.Index).ToList())!.Value;
            source = BenchmarkBatteryReproducibilitySource.Rounds;
            reproSe = sd / Math.Sqrt(r);
            reproDf = r - 1;
            reproCrit = BenchmarkGroupStatistics.StudentTCritical95(r - 1);
            reproHalf = reproCrit.Value * reproSe.Value;
        }
        else if (!aligned && stats.All(s => s.Index.ReproducibilityStandardError.HasValue))
        {
            var se = stats.Select(s => s.Index.ReproducibilityStandardError!.Value).ToList();
            source = BenchmarkBatteryReproducibilitySource.PerSuiteFallback;
            reproSe = Math.Sqrt(Enumerable.Range(0, k).Sum(s => w[s] * w[s] * se[s] * se[s]));
            reproDf = SatterthwaiteDegreesOfFreedom(w, se, stats.Select(s => s.Index.RunCount - 1).ToList());
            reproCrit = BenchmarkGroupStatistics.StudentTCritical95(FloorDegreesOfFreedom(reproDf.Value));
            reproHalf = reproSe.Value > 0.0 ? reproCrit.Value * reproSe.Value : 0.0;
            caveats.Add(PerSuiteFallbackCaveat);
        }
        else
        {
            caveats.Add(NoReproducibilityCaveat);
        }

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

        return new BenchmarkBatteryOverallIndex
        {
            PointEstimate = point,
            ItemSamplingStandardError = itemSe,
            EffectiveDegreesOfFreedom = itemDf,
            ItemSamplingCriticalValue = itemCrit,
            ItemSamplingHalfWidth = itemHalf,
            ItemSamplingWithheldBySuiteIndex = itemWithheld,
            ReproducibilityStandardError = reproSe,
            ReproducibilityDegreesOfFreedom = reproDf,
            ReproducibilityCriticalValue = reproCrit,
            ReproducibilityHalfWidth = reproHalf,
            ReproducibilitySource = source,
            RoundCount = aligned ? perRound.Count : 0,
            PerRoundIndices = perRound,
            CombinedHalfWidth = combined,
            CombinedLower = combined.HasValue ? ClampScore(point - combined.Value) : null,
            CombinedUpper = combined.HasValue ? ClampScore(point + combined.Value) : null,
            CombinedIntervalTruncated = combined.HasValue
                && (point - combined.Value < BenchmarkGroupStatistics.MinScore
                    || point + combined.Value > BenchmarkGroupStatistics.MaxScore)
        };
    }

    private static BenchmarkBatterySpeedStatistics ComputeSpeed(
        IReadOnlyList<BenchmarkBatteryDefinitionSuite> defs,
        IReadOnlyList<BenchmarkBatterySuiteInput> inputs,
        IReadOnlyList<BenchmarkGroupStatisticsResult> stats,
        IReadOnlyList<double> v,
        BenchmarkBatteryStatisticsOptions cfg,
        List<string> caveats)
    {
        int k = stats.Count;
        var withheld = new List<int>();
        double overall = 0.0;
        for (int s = 0; s < k; s++)
        {
            double? mean = stats[s].Speed.MeanSpeedIndex;
            if (mean.HasValue)
            {
                overall += v[s] * mean.Value;
            }
            else
            {
                withheld.Add(defs[s].Index);
                caveats.Add($"Overall Speed Index withheld: suite '{defs[s].SuiteName}' has no Speed Index.");
            }
        }

        var modelTimes = inputs.SelectMany(i => i.ModelTimesMs ?? Array.Empty<double>()).ToList();
        var ttft = inputs.SelectMany(i => i.TtftMs ?? Array.Empty<double>()).ToList();

        var reasons = new List<string>();
        if (cfg.SpeedDegraded)
        {
            reasons.Add(string.IsNullOrWhiteSpace(cfg.SpeedDegradedReason)
                ? "a key affecting timing differs between suites"
                : cfg.SpeedDegradedReason!.Trim());
        }

        for (int s = 0; s < k; s++)
        {
            if (!stats[s].Speed.Degraded) continue;
            reasons.Add($"suite '{defs[s].SuiteName}': "
                + (string.IsNullOrWhiteSpace(stats[s].Speed.DegradedReason) ? "timing conditions differ" : stats[s].Speed.DegradedReason!.Trim()));
        }

        return new BenchmarkBatterySpeedStatistics
        {
            OverallSpeedIndex = withheld.Count == 0 ? overall : null,
            SpeedIndexWithheldBySuiteIndex = withheld,
            PooledAnswerCount = modelTimes.Count,
            ModelTimeP50Ms = BenchmarkGroupStatistics.Percentile(modelTimes, 50.0),
            ModelTimeP90Ms = BenchmarkGroupStatistics.Percentile(modelTimes, 90.0),
            ModelTimeMaxMs = modelTimes.Count > 0 ? modelTimes.Max() : null,
            ModelTimeMeanMs = modelTimes.Count > 0 ? modelTimes.Average() : null,
            TotalModelTimeMs = modelTimes.Sum(),
            TtftAnswerCount = ttft.Count,
            TtftP50Ms = BenchmarkGroupStatistics.Percentile(ttft, 50.0),
            TtftP90Ms = BenchmarkGroupStatistics.Percentile(ttft, 90.0),
            TtftMaxMs = ttft.Count > 0 ? ttft.Max() : null,
            Degraded = reasons.Count > 0,
            DegradedReason = reasons.Count > 0 ? string.Join("; ", reasons) : null
        };
    }

    private static BenchmarkBatteryCostStatistics ComputeCost(
        IReadOnlyList<BenchmarkBatteryDefinitionSuite> defs,
        IReadOnlyList<BenchmarkBatterySuiteInput> inputs,
        IReadOnlyList<BenchmarkGroupStatisticsResult> stats,
        double point,
        BenchmarkBatteryStatisticsOptions cfg,
        List<string> caveats)
    {
        int k = stats.Count;

        var reasons = new List<string>();
        if (cfg.CostDegraded)
        {
            reasons.Add(string.IsNullOrWhiteSpace(cfg.CostDegradedReason)
                ? "a key affecting cost differs between suites"
                : cfg.CostDegradedReason!.Trim());
        }

        for (int s = 0; s < k; s++)
        {
            if (stats[s].Cost?.Degraded != true) continue;
            reasons.Add($"suite '{defs[s].SuiteName}': "
                + (string.IsNullOrWhiteSpace(stats[s].Cost!.DegradedReason) ? "pricing conditions differ" : stats[s].Cost!.DegradedReason!.Trim()));
        }

        bool degraded = reasons.Count > 0;
        string? degradedReason = degraded ? string.Join("; ", reasons) : null;

        // The group layer omits a run whose pricing did not resolve, so a short cost row count is
        // an unknown, not a smaller total.
        var withheld = new List<int>();
        var withheldNames = new List<string>();
        for (int s = 0; s < k; s++)
        {
            var c = stats[s].Cost;
            if (c == null || c.RunCount < inputs[s].UsableMemberCount)
            {
                withheld.Add(defs[s].Index);
                withheldNames.Add($"'{defs[s].SuiteName}'");
            }
        }

        if (withheld.Count > 0)
        {
            string reason = "Cost unknown: pricing did not resolve for every usable member of suite "
                + string.Join(", ", withheldNames) + ".";
            caveats.Add(reason);
            return new BenchmarkBatteryCostStatistics
            {
                Available = false,
                WithheldBySuiteIndex = withheld,
                WithheldReason = reason,
                Degraded = degraded,
                DegradedReason = degradedReason
            };
        }

        var costs = stats.Select(s => s.Cost!).ToList();
        double total = costs.Sum(c => c.TotalCost);
        double pass = costs.Sum(c => c.MeanCostPerRun);

        var totalByRole = new Dictionary<string, double>(StringComparer.Ordinal);
        var passByRole = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var c in costs)
        {
            foreach (var kv in c.TotalCostByRole)
            {
                totalByRole[kv.Key] = totalByRole.GetValueOrDefault(kv.Key) + kv.Value;
            }

            foreach (var kv in c.MeanCostByRole)
            {
                passByRole[kv.Key] = passByRole.GetValueOrDefault(kv.Key) + kv.Value;
            }
        }

        int? answerRows = null;
        if (costs.All(c => c.QuestionsAskedPerRun.HasValue))
        {
            answerRows = (int)Math.Round(costs.Sum(c => c.QuestionsAskedPerRun!.Value * c.RunCount), MidpointRounding.AwayFromZero);
        }

        return new BenchmarkBatteryCostStatistics
        {
            Available = true,
            TotalCost = total,
            PassCost = pass,
            TotalCostByRole = totalByRole,
            PassCostByRole = passByRole,
            AnswerRowCount = answerRows,
            CostPerQuestion = answerRows is > 0 ? total / answerRows.Value : null,
            CostPerIndexPoint = point > 0.0 ? pass / point : null,
            Degraded = degraded,
            DegradedReason = degradedReason
        };
    }

    private static BenchmarkBatteryUsageStatistics? ComputeUsage(IReadOnlyList<BenchmarkBatterySuiteInput> inputs)
    {
        var usages = inputs
            .Select(i => i.Statistics?.Usage)
            .Where(u => u != null)
            .Select(u => u!)
            .ToList();

        if (usages.Count == 0) return null;

        var byFamily = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var usage in usages)
        {
            foreach (var kv in usage.ToolCallsByFamily)
            {
                byFamily[kv.Key] = byFamily.GetValueOrDefault(kv.Key) + kv.Value;
            }
        }

        var modelCalls = usages.Where(u => u.TotalModelCalls.HasValue).Select(u => u.TotalModelCalls!.Value).ToList();

        return new BenchmarkBatteryUsageStatistics
        {
            TotalInputTokens = usages.Sum(u => u.TotalInputTokens),
            TotalOutputTokens = usages.Sum(u => u.TotalOutputTokens),
            TotalCacheReadTokens = usages.Sum(u => u.TotalCacheReadTokens),
            TotalAssessmentInputTokens = usages.Sum(u => u.TotalAssessmentInputTokens),
            TotalAssessmentOutputTokens = usages.Sum(u => u.TotalAssessmentOutputTokens),
            TotalClaimVerificationInputTokens = usages.Sum(u => u.TotalClaimVerificationInputTokens),
            TotalClaimVerificationOutputTokens = usages.Sum(u => u.TotalClaimVerificationOutputTokens),
            TotalToolCalls = usages.Sum(u => u.TotalToolCalls),
            TotalModelCalls = modelCalls.Count > 0 ? modelCalls.Sum() : null,
            ToolCallsByFamily = byFamily,
            ClaimsSupported = usages.Sum(u => u.ClaimsSupported),
            ClaimsRefuted = usages.Sum(u => u.ClaimsRefuted),
            ClaimsIndeterminate = usages.Sum(u => u.ClaimsIndeterminate),
            ClaimsChecked = usages.Sum(u => u.ClaimsChecked)
        };
    }

    // --- Comparison -----------------------------------------------------------------------------

    /// <summary>
    /// Compares two battery results of one definition (M7), paired by question within each suite on
    /// equal item revisions. The caller has already established eligibility
    /// (<c>BenchmarkBatteryComparability.CanCompare</c>); the weights <c>w_s</c> and <c>d_q</c> are
    /// the baseline's.
    /// </summary>
    public static BenchmarkBatteryComparison Compare(
        BenchmarkBatteryStatisticsResult baseline,
        BenchmarkBatteryStatisticsResult treatment,
        BenchmarkBatteryCompareOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(treatment);
        var cfg = options ?? new BenchmarkBatteryCompareOptions();

        if (baseline.Suites.Count != treatment.Suites.Count)
        {
            throw new ArgumentException("The two battery results have different numbers of suites.", nameof(treatment));
        }

        var treatmentByIndex = treatment.Suites.ToDictionary(s => s.SuiteIndex);
        var notes = new List<string>();
        var rows = new List<BenchmarkBatterySuiteComparison>();
        var compositeWithheld = new List<int>();
        var seWithheld = new List<int>();

        var terms = new List<(double Weight, double Delta, double? Se, int Pairs)>();
        var contributions = new List<double>();

        foreach (var b in baseline.Suites.OrderBy(s => s.SuiteIndex))
        {
            if (!treatmentByIndex.TryGetValue(b.SuiteIndex, out var t))
            {
                throw new ArgumentException($"The treatment has no suite at index {b.SuiteIndex}.", nameof(treatment));
            }

            if (b.Statistics == null || t.Statistics == null)
            {
                string note = $"Suite '{b.SuiteName}' has no usable member on "
                    + (b.Statistics == null && t.Statistics == null ? "either side" : b.Statistics == null ? "the baseline" : "the treatment")
                    + ", so no composite difference is reported.";
                notes.Add(note);
                compositeWithheld.Add(b.SuiteIndex);
                rows.Add(new BenchmarkBatterySuiteComparison
                {
                    SuiteIndex = b.SuiteIndex,
                    SuiteName = b.SuiteName,
                    Weight = b.Weight,
                    Note = note
                });
                continue;
            }

            var group = BenchmarkGroupStatistics.Compare(b.Statistics, t.Statistics, cfg.FalseDiscoveryRate);
            var pairs = PairItems(b.Statistics, t.Statistics);

            double? delta = null;
            double? se = null;
            string? suiteNote = null;

            if (pairs.Count == 0)
            {
                suiteNote = $"Suite '{b.SuiteName}' has no paired item, so no composite difference is reported.";
                notes.Add(suiteNote);
                compositeWithheld.Add(b.SuiteIndex);
            }
            else
            {
                double sumD = pairs.Sum(p => p.Weight);
                delta = pairs.Sum(p => p.Weight * p.Delta) / sumD;
                se = UnroundedWeightedStandardError(pairs.Select(p => (Value: p.Delta, Weight: p.Weight)).ToList());
                if (!se.HasValue)
                {
                    seWithheld.Add(b.SuiteIndex);
                    notes.Add($"Suite '{b.SuiteName}' has fewer than three paired items, so the composite standard error is withheld.");
                }

                if (b.Weight.HasValue)
                {
                    terms.Add((b.Weight.Value, delta.Value, se, pairs.Count));
                    contributions.AddRange(pairs.Select(p => b.Weight.Value * p.Weight / sumD * p.Delta));
                }
                else
                {
                    compositeWithheld.Add(b.SuiteIndex);
                    notes.Add($"Suite '{b.SuiteName}' has no declared weight on the baseline, so no composite difference is reported.");
                }
            }

            rows.Add(new BenchmarkBatterySuiteComparison
            {
                SuiteIndex = b.SuiteIndex,
                SuiteName = b.SuiteName,
                Weight = b.Weight,
                PairedItemCount = pairs.Count,
                WeightedDifference = delta,
                WeightedDifferenceStandardError = se,
                WilcoxonPValue = group.Wilcoxon.PValue,
                Comparison = group,
                Note = suiteNote
            });
        }

        // Holm across the suites that have a Wilcoxon p; a null p is left out of the family.
        var testable = rows.Select((r, i) => (Row: r, Position: i)).Where(x => x.Row.WilcoxonPValue.HasValue).ToList();
        var holm = HolmAdjust(testable.Select(x => x.Row.WilcoxonPValue!.Value).ToList());
        for (int i = 0; i < testable.Count; i++)
        {
            rows[testable[i].Position] = testable[i].Row with { HolmAdjustedPValue = holm[i] };
        }

        double? d = null;
        double? compositeSe = null;
        double? df = null;
        double? crit = null;
        double? half = null;
        double? pValue = null;
        var method = BenchmarkBatteryRandomizationMethod.NotComputed;
        int? resamples = null;
        double? mcSe = null;
        int? seed = null;

        if (compositeWithheld.Count == 0 && terms.Count > 0)
        {
            d = terms.Sum(x => x.Weight * x.Delta);

            if (seWithheld.Count == 0)
            {
                var weights = terms.Select(x => x.Weight).ToList();
                var ses = terms.Select(x => x.Se!.Value).ToList();
                compositeSe = Math.Sqrt(terms.Sum(x => x.Weight * x.Weight * x.Se!.Value * x.Se!.Value));
                df = SatterthwaiteDegreesOfFreedom(weights, ses, terms.Select(x => x.Pairs - 1).ToList());
                crit = BenchmarkGroupStatistics.StudentTCritical95(FloorDegreesOfFreedom(df.Value));
                half = compositeSe.Value > 0.0 ? crit.Value * compositeSe.Value : 0.0;
            }

            if (contributions.Count <= Math.Min(cfg.MaxExactPairedItems, MaxEnumerablePairedItems))
            {
                pValue = ExactSignFlipPValue(contributions);
                method = BenchmarkBatteryRandomizationMethod.Exact;
            }
            else
            {
                int resampleCount = Math.Max(1, cfg.MonteCarloResamples);
                (pValue, mcSe) = MonteCarloSignFlipPValue(contributions, resampleCount, cfg.Seed);
                method = BenchmarkBatteryRandomizationMethod.MonteCarlo;
                resamples = resampleCount;
                seed = cfg.Seed;
            }
        }

        notes.Add("Per-suite Wilcoxon p-values are Holm-adjusted across the suites that have one; "
            + "per-item comparisons inside each suite remain exploratory.");

        return new BenchmarkBatteryComparison
        {
            BaselineOverallIndex = baseline.OverallIndex?.PointEstimate,
            TreatmentOverallIndex = treatment.OverallIndex?.PointEstimate,
            Suites = rows,
            PairedItemCount = rows.Sum(r => r.PairedItemCount),
            CompositeDifference = d,
            CompositeWithheldBySuiteIndex = compositeWithheld,
            CompositeStandardError = compositeSe,
            StandardErrorWithheldBySuiteIndex = seWithheld,
            CompositeDegreesOfFreedom = df,
            CompositeCriticalValue = crit,
            CompositeConfidenceHalfWidth = half,
            CompositeConfidenceLower = d.HasValue && half.HasValue ? d.Value - half.Value : null,
            CompositeConfidenceUpper = d.HasValue && half.HasValue ? d.Value + half.Value : null,
            RandomizationPValue = pValue,
            RandomizationMethod = method,
            MonteCarloResamples = resamples,
            MonteCarloStandardError = mcSe,
            Seed = seed,
            Notes = notes
        };
    }

    /// <summary>
    /// Items present on both sides under equal item revisions, in baseline order:
    /// <c>Δ_q</c> = treatment mean − baseline mean, <c>d_q</c> = baseline weight.
    /// </summary>
    private static List<(double Delta, double Weight)> PairItems(
        BenchmarkGroupStatisticsResult baseline,
        BenchmarkGroupStatisticsResult treatment)
    {
        var treatmentByQuestion = treatment.Items.ToDictionary(i => i.QuestionId);
        return baseline.Items
            .Where(b => treatmentByQuestion.TryGetValue(b.QuestionId, out var t) && t.ItemRevision == b.ItemRevision)
            .OrderBy(b => b.OrderIndex)
            .ThenBy(b => b.QuestionId)
            .Select(b => (Delta: treatmentByQuestion[b.QuestionId].Mean - b.Mean, Weight: b.Weight))
            .ToList();
    }

    // --- Tests and helpers ----------------------------------------------------------------------

    /// <summary>
    /// Holm's step-down adjustment: sort ascending, <c>p̃(i) = max over j ≤ i of min(1, (m − j + 1) · p(j))</c>.
    /// Results come back in input order.
    /// </summary>
    public static IReadOnlyList<double> HolmAdjust(IReadOnlyList<double> pValues)
    {
        var p = pValues ?? Array.Empty<double>();
        int m = p.Count;
        if (m == 0) return Array.Empty<double>();

        var order = Enumerable.Range(0, m).OrderBy(i => p[i]).ThenBy(i => i).ToList();
        var adjusted = new double[m];
        double running = 0.0;
        for (int rank = 0; rank < m; rank++)
        {
            int idx = order[rank];
            running = Math.Max(running, Math.Min(1.0, (m - rank) * p[idx]));
            adjusted[idx] = running;
        }

        return adjusted;
    }

    /// <summary>
    /// Welch–Satterthwaite effective degrees of freedom of <c>Σ w_i · X_i</c>:
    /// <c>(Σ w_i² SE_i²)² / Σ (w_i⁴ SE_i⁴ / df_i)</c>, unfloored. When every term is zero the ratio
    /// is 0/0 and <c>Σ df_i</c> is returned. A non-zero term on a non-positive <c>df</c> returns 0.
    /// </summary>
    public static double SatterthwaiteDegreesOfFreedom(
        IReadOnlyList<double> weights,
        IReadOnlyList<double> standardErrors,
        IReadOnlyList<int> degreesOfFreedom)
    {
        ArgumentNullException.ThrowIfNull(weights);
        ArgumentNullException.ThrowIfNull(standardErrors);
        ArgumentNullException.ThrowIfNull(degreesOfFreedom);
        if (weights.Count != standardErrors.Count || weights.Count != degreesOfFreedom.Count)
        {
            throw new ArgumentException("Weights, standard errors and degrees of freedom must have equal lengths.");
        }

        double numerator = 0.0;
        double denominator = 0.0;
        int dfSum = 0;
        for (int i = 0; i < weights.Count; i++)
        {
            double variance = weights[i] * weights[i] * standardErrors[i] * standardErrors[i];
            numerator += variance;
            dfSum += Math.Max(0, degreesOfFreedom[i]);

            if (variance > 0.0)
            {
                if (degreesOfFreedom[i] <= 0) return 0.0;
                denominator += variance * variance / degreesOfFreedom[i];
            }
        }

        if (numerator <= 0.0 || denominator <= 0.0) return dfSum;
        return numerator * numerator / denominator;
    }

    /// <summary>
    /// <see cref="BenchmarkScoring.QualityIndexStandardError(IEnumerable{ValueTuple{double?, int?}})"/>
    /// without its rounding: centered on the unrounded weighted mean and taking real-valued weights,
    /// so it applies to differences that are negative and small.
    /// <c>√(Σ w² (x − x̄_w)² · m/(m−1)) / Σ w</c>, weights floored at 1. Null below three items.
    /// </summary>
    internal static double? UnroundedWeightedStandardError(IReadOnlyList<(double Value, double Weight)> items)
    {
        if (items == null || items.Count < 3) return null;

        double weightSum = 0.0;
        double weighted = 0.0;
        foreach (var (value, weight) in items)
        {
            double w = Math.Max(1.0, weight);
            weightSum += w;
            weighted += w * value;
        }

        if (weightSum <= 0.0) return null;
        double mean = weighted / weightSum;

        double sumSq = 0.0;
        foreach (var (value, weight) in items)
        {
            double w = Math.Max(1.0, weight);
            double dev = value - mean;
            sumSq += w * w * dev * dev;
        }

        int m = items.Count;
        return Math.Sqrt(sumSq * m / (m - 1)) / weightSum;
    }

    /// <summary>
    /// The exact two-sided sign-flip p over contributions <c>a_q = c_q Δ_q</c>: the share of the
    /// <c>2^N</c> sign assignments with <c>|Σ ε_q a_q| ≥ |Σ a_q| − </c><see cref="TieTolerance"/>.
    /// Assignments are visited in Gray-code order; each sum is recomputed in a fixed order, so an
    /// assignment and its mirror produce exactly opposite sums.
    /// </summary>
    private static double ExactSignFlipPValue(IReadOnlyList<double> contributions)
    {
        int n = contributions.Count;
        var signs = new double[n];
        Array.Fill(signs, 1.0);

        double threshold = Math.Abs(SignedSum(contributions, signs)) - TieTolerance;
        long total = 1L << n;
        long count = 0;

        for (long step = 0; step < total; step++)
        {
            if (step > 0)
            {
                int bit = BitOperations.TrailingZeroCount(step);
                signs[bit] = -signs[bit];
            }

            if (Math.Abs(SignedSum(contributions, signs)) >= threshold) count++;
        }

        return count / (double)total;
    }

    /// <summary>
    /// The Monte Carlo sign-flip p from <paramref name="resamples"/> seeded resamples,
    /// <c>(1 + #{|D*| ≥ |D|}) / (B + 1)</c> (Phipson and Smyth, 2010), and its Monte Carlo standard
    /// error <c>√(p(1 − p) / B)</c>.
    /// </summary>
    private static (double PValue, double StandardError) MonteCarloSignFlipPValue(
        IReadOnlyList<double> contributions,
        int resamples,
        int seed)
    {
        int n = contributions.Count;
        var signs = new double[n];
        Array.Fill(signs, 1.0);
        double threshold = Math.Abs(SignedSum(contributions, signs)) - TieTolerance;

        var rng = new Random(seed);
        long count = 0;
        for (int b = 0; b < resamples; b++)
        {
            for (int j = 0; j < n; j++)
            {
                signs[j] = rng.Next(2) == 0 ? 1.0 : -1.0;
            }

            if (Math.Abs(SignedSum(contributions, signs)) >= threshold) count++;
        }

        double p = (1.0 + count) / (resamples + 1.0);
        return (p, Math.Sqrt(p * (1.0 - p) / resamples));
    }

    private static double SignedSum(IReadOnlyList<double> values, double[] signs)
    {
        double sum = 0.0;
        for (int j = 0; j < values.Count; j++)
        {
            sum += signs[j] * values[j];
        }

        return sum;
    }

    private static double WeightedSum(IReadOnlyList<double> weights, IReadOnlyList<double> values)
    {
        double sum = 0.0;
        for (int i = 0; i < weights.Count; i++)
        {
            sum += weights[i] * values[i];
        }

        return sum;
    }

    /// <summary>ν floored to an integer for the t table, never below 1.</summary>
    private static int FloorDegreesOfFreedom(double degreesOfFreedom)
    {
        if (double.IsNaN(degreesOfFreedom) || degreesOfFreedom < 1.0) return 1;
        if (degreesOfFreedom >= int.MaxValue) return int.MaxValue;
        return Math.Max(1, (int)Math.Floor(degreesOfFreedom + IdentityTolerance));
    }

    private static double ClampScore(double value)
        => Math.Clamp(value, BenchmarkGroupStatistics.MinScore, BenchmarkGroupStatistics.MaxScore);

    /// <summary>
    /// The weights of one scheme, or null when they cannot be computed — wrong count, a non-finite
    /// or negative value, or a refusal from the definition — so that no NaN reaches a persisted result.
    /// </summary>
    private static IReadOnlyList<double>? TryWeights(Func<IReadOnlyList<double>> compute, int count)
    {
        IReadOnlyList<double> weights;
        try
        {
            weights = compute();
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (DivideByZeroException)
        {
            return null;
        }

        if (weights == null || weights.Count != count || weights.Any(w => !double.IsFinite(w) || w < 0.0))
        {
            return null;
        }

        return weights.ToList();
    }
}
