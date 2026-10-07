namespace Overseer.Services.ChatConsistency;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Overseer.Services.Benchmarking;

/// <summary>
/// The outcome of comparing two periods against a smallest effect size of interest: Lakens'
/// four outcomes of combining a difference test with an equivalence test, with the "changed"
/// outcome split by direction (Lakens, Scheel &amp; Isager 2018).
/// </summary>
public enum ConsistencyVerdict
{
    /// <summary>Significant, and the 95 % interval lies wholly beyond the margin on the worse side.</summary>
    ChangedDegraded,

    /// <summary>Significant, and the 95 % interval lies wholly beyond the margin on the better side.</summary>
    ChangedImproved,

    /// <summary>The 95 % interval excludes zero but lies wholly inside the margin.</summary>
    ChangedNegligible,

    /// <summary>The 90 % interval lies inside the margin (TOST).</summary>
    Equivalent,

    /// <summary>None of the above: the data cannot tell a change from no change.</summary>
    Inconclusive
}

/// <summary>The statistic a resampling or stratified estimate is computed with.</summary>
public enum BootstrapStatistic
{
    /// <summary>
    /// One sample: the median of the Walsh averages. Two samples: the median of all pairwise
    /// differences, treatment minus baseline (Hodges &amp; Lehmann 1963).
    /// </summary>
    HodgesLehmann,

    /// <summary>One sample: the mean. Two samples: the difference of the means.</summary>
    Mean,

    /// <summary>One sample: the median. Two samples: the difference of the medians.</summary>
    Median
}

/// <summary>One of the twelve time-of-week strata: a 4-hour UTC block on a weekday or a weekend day.</summary>
/// <param name="Index">0–5 for the weekday blocks 00–04 … 20–24 UTC, 6–11 for the same weekend blocks.</param>
/// <param name="Label">For example "Weekday 08–12 UTC".</param>
public readonly record struct ConsistencyStratum(int Index, string Label);

/// <summary>One observation tagged with its stratum.</summary>
public readonly record struct StratifiedObservation(int Stratum, double Value);

/// <summary>The common-support, equal-weight stratified shift between two periods.</summary>
public sealed record StratifiedShiftResult
{
    /// <summary>The equal-weight mean of the per-stratum shifts. Null when no stratum is common to both periods.</summary>
    public double? Shift { get; init; }

    /// <summary>The strata present in both periods, ascending.</summary>
    public IReadOnlyList<int> StrataUsed { get; init; } = Array.Empty<int>();

    /// <summary>The shift (treatment minus baseline) within each stratum used.</summary>
    public IReadOnlyDictionary<int, double> StratumShifts { get; init; } = new Dictionary<int, double>();

    /// <summary>Observations in both periods together.</summary>
    public int TotalObservationCount { get; init; }

    /// <summary>Observations in strata present in only one period.</summary>
    public int ExcludedObservationCount { get; init; }

    /// <summary><see cref="ExcludedObservationCount"/> over <see cref="TotalObservationCount"/>; 0 when there are no observations.</summary>
    public double ExcludedShare { get; init; }
}

/// <summary>
/// A bootstrap distribution: the point estimate on the original data and the sorted replicate
/// values, from which percentile intervals at any level are read (Davison &amp; Hinkley 1997, § 5.3).
/// </summary>
public sealed class BootstrapDistribution
{
    internal BootstrapDistribution(double estimate, IEnumerable<double> replicates, int requestedReplicates, IReadOnlyList<int> clusterCounts)
    {
        Estimate = estimate;
        Replicates = replicates.Where(v => !double.IsNaN(v)).OrderBy(v => v).ToArray();
        RequestedReplicates = requestedReplicates;
        ClusterCounts = clusterCounts;
    }

    /// <summary>The statistic on the original data.</summary>
    public double Estimate { get; }

    /// <summary>The replicate values that were defined, ascending.</summary>
    public IReadOnlyList<double> Replicates { get; }

    /// <summary>The number of replicates drawn, including any that yielded no value.</summary>
    public int RequestedReplicates { get; }

    /// <summary>The number of clusters (runs) in each resampled group, in the order the groups were passed.</summary>
    public IReadOnlyList<int> ClusterCounts { get; }

    /// <summary>The 95 % percentile interval.</summary>
    public (double Lower, double Upper) Interval95 => Interval(0.95);

    /// <summary>The 90 % percentile interval, the one a TOST at α = 0.05 reads.</summary>
    public (double Lower, double Upper) Interval90 => Interval(0.90);

    /// <summary>
    /// The two-sided percentile interval at <paramref name="level"/>: the α/2 and 1 − α/2
    /// quantiles of the replicates, by linear interpolation between order statistics
    /// (Davison &amp; Hinkley 1997, § 5.3). NaN bounds when no replicate was defined.
    /// </summary>
    public (double Lower, double Upper) Interval(double level)
    {
        if (!(level > 0.0 && level < 1.0))
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "The confidence level must lie strictly between 0 and 1.");
        }

        if (Replicates.Count == 0) return (double.NaN, double.NaN);

        double tail = (1.0 - level) / 2.0;
        double lower = BenchmarkGroupStatistics.Percentile(Replicates, 100.0 * tail)!.Value;
        double upper = BenchmarkGroupStatistics.Percentile(Replicates, 100.0 * (1.0 - tail))!.Value;
        return (lower, upper);
    }
}

/// <summary>One decile of a shift function.</summary>
/// <param name="Probability">0.1 … 0.9.</param>
/// <param name="QuantileX">The Harrell–Davis decile of the baseline sample.</param>
/// <param name="QuantileY">The Harrell–Davis decile of the treatment sample.</param>
/// <param name="Difference"><paramref name="QuantileY"/> − <paramref name="QuantileX"/>.</param>
/// <param name="Lower">The lower bound of the pointwise percentile bootstrap interval.</param>
/// <param name="Upper">The upper bound of the pointwise percentile bootstrap interval.</param>
public sealed record ShiftFunctionDecile(double Probability, double QuantileX, double QuantileY, double Difference, double Lower, double Upper);

/// <summary>
/// One arm of an item difference-in-differences: the runs of each period, each run a map from
/// item key to that item's value in the caller's units (index points, log values, …).
/// </summary>
public sealed record DifferenceInDifferencesArm(
    IReadOnlyList<IReadOnlyDictionary<string, double>> Before,
    IReadOnlyList<IReadOnlyDictionary<string, double>> After);

/// <summary>One item's difference-in-differences, from the cross-run item means of each cell.</summary>
public sealed record ItemDifferenceInDifferencesRow(string ItemKey, double TargetChange, double ControlChange, double Difference);

/// <summary>An item difference-in-differences with its run-cluster bootstrap distribution.</summary>
public sealed record ItemDifferenceInDifferencesResult
{
    /// <summary>Per item, ordered by key (ordinal).</summary>
    public IReadOnlyList<ItemDifferenceInDifferencesRow> Items { get; init; } = Array.Empty<ItemDifferenceInDifferencesRow>();

    /// <summary>The number of items present in all four cells.</summary>
    public int ItemCount => Items.Count;

    /// <summary>The statistic over the per-item differences, and its bootstrap distribution.</summary>
    public required BootstrapDistribution Distribution { get; init; }
}

/// <summary>A minimum detectable effect and the noise it was computed from.</summary>
public sealed record MinimumDetectableEffectResult
{
    /// <summary>The note attached when the noise had to come from item spread.</summary>
    public const string OneRunPerPeriodNote = "one run per period: run-to-run noise not estimable";

    /// <summary>The smallest true difference detectable at the stated α and power.</summary>
    public double Effect { get; init; }

    /// <summary>The pooled standard deviation used.</summary>
    public double StandardDeviation { get; init; }

    /// <summary>The baseline count the effect was computed for: runs, or items when <see cref="CapNote"/> is set.</summary>
    public int BaselineCount { get; init; }

    /// <summary>The treatment count the effect was computed for: runs, or items when <see cref="CapNote"/> is set.</summary>
    public int TreatmentCount { get; init; }

    /// <summary>True when a period had fewer than two runs, so the noise is the item standard deviation.</summary>
    public bool CapNote { get; init; }

    /// <summary><see cref="OneRunPerPeriodNote"/> when <see cref="CapNote"/> is set, else null.</summary>
    public string? Note { get; init; }

    /// <summary>The two-sided significance level.</summary>
    public double Alpha { get; init; }

    /// <summary>The power.</summary>
    public double Power { get; init; }
}

/// <summary>
/// Statistics for comparing the production chat between two periods. Pure: no I/O, no clock,
/// invariant culture, and every random draw comes from a <see cref="Random"/> seeded by a
/// parameter, so fixed inputs give fixed results.
/// </summary>
public static class ChatConsistencyStatistics
{
    /// <summary>The default bootstrap seed.</summary>
    public const int DefaultSeed = 20261007;

    /// <summary>The default number of bootstrap replicates.</summary>
    public const int DefaultBootstrapReplicates = 10_000;

    /// <summary>The default two-sided significance level.</summary>
    public const double DefaultAlpha = 0.05;

    /// <summary>The default power of a minimum-detectable-effect calculation.</summary>
    public const double DefaultPower = 0.8;

    /// <summary>The number of time-of-week strata.</summary>
    public const int StratumCount = 12;

    // The largest pairwise set a Hodges-Lehmann estimate materializes.
    private const long MaxPairwiseCount = 100_000_000;

    // Relative tolerance when comparing hypergeometric probabilities with the observed one.
    private const double FisherRelativeTolerance = 1e-7;

    // --- Hodges-Lehmann --------------------------------------------------------------------------

    /// <summary>
    /// The one-sample Hodges–Lehmann estimator of a paired shift: the median of the Walsh
    /// averages <c>(dᵢ + dⱼ) / 2</c>, <c>i ≤ j</c> (Hodges &amp; Lehmann 1963). Null over an
    /// empty sample.
    /// </summary>
    public static double? HodgesLehmannShift(IReadOnlyList<double> differences)
    {
        RequireFinite(differences, nameof(differences));
        if (differences.Count == 0) return null;

        var values = differences.ToArray();
        double[]? scratch = null;
        return OneSampleHodgesLehmann(values, values.Length, ref scratch);
    }

    /// <summary>
    /// The two-sample Hodges–Lehmann shift: the median of all pairwise differences
    /// <c>yⱼ − xᵢ</c>, so a positive value means <paramref name="y"/> lies higher
    /// (Hodges &amp; Lehmann 1963). Null when either sample is empty.
    /// </summary>
    public static double? HodgesLehmannShift(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        RequireFinite(x, nameof(x));
        RequireFinite(y, nameof(y));
        if (x.Count == 0 || y.Count == 0) return null;

        var xs = x.ToArray();
        var ys = y.ToArray();
        double[]? scratch = null;
        return TwoSampleHodgesLehmann(xs, xs.Length, ys, ys.Length, ref scratch);
    }

    // --- Cluster bootstrap -----------------------------------------------------------------------

    /// <summary>
    /// The cluster bootstrap of a one-sample statistic over clusters (runs) of item-level values
    /// or paired differences. Each replicate draws as many runs as there are, with replacement;
    /// with <paramref name="resampleItems"/> it then draws each drawn run's items with replacement
    /// (the two-stage bootstrap, Davison &amp; Hinkley 1997, § 3.8), otherwise it keeps each drawn
    /// run whole, which is the one-level form for speed endpoints. The statistic is taken over the
    /// pooled values. Resampling whole runs keeps within-run correlation in every replicate
    /// (Cameron, Gelbach &amp; Miller 2008), without which intervals come out too narrow
    /// (Bertrand, Duflo &amp; Mullainathan 2004).
    /// <para>Empty clusters are ignored. Null when no value remains.</para>
    /// </summary>
    public static BootstrapDistribution? ClusterBootstrap(
        IReadOnlyList<IReadOnlyList<double>> clusters,
        BootstrapStatistic statistic = BootstrapStatistic.HodgesLehmann,
        bool resampleItems = true,
        int replicates = DefaultBootstrapReplicates,
        int seed = DefaultSeed)
    {
        var data = PrepareClusters(clusters, nameof(clusters));
        RequireReplicates(replicates);
        if (data.Length == 0) return null;

        var pooled = data.SelectMany(c => c).ToArray();
        double[]? scratch = null;
        double estimate = OneSample(statistic, pooled, pooled.Length, ref scratch);

        var rng = new Random(seed);
        var buffer = new double[ResampleCapacity(data)];
        var values = new double[replicates];
        for (int b = 0; b < replicates; b++)
        {
            int count = DrawClusters(data, rng, resampleItems, buffer);
            values[b] = OneSample(statistic, buffer, count, ref scratch);
        }

        return new BootstrapDistribution(estimate, values, replicates, new[] { data.Length });
    }

    /// <summary>
    /// The cluster bootstrap of a two-sample shift, treatment minus baseline, between two
    /// independent sets of clusters (runs). Each replicate resamples the baseline runs and the
    /// treatment runs separately, as <see cref="ClusterBootstrap"/> does for one set
    /// (Davison &amp; Hinkley 1997, § 3.8; Cameron, Gelbach &amp; Miller 2008).
    /// <para>Null when either side has no value.</para>
    /// </summary>
    public static BootstrapDistribution? ClusterBootstrapTwoSample(
        IReadOnlyList<IReadOnlyList<double>> baselineClusters,
        IReadOnlyList<IReadOnlyList<double>> treatmentClusters,
        BootstrapStatistic statistic = BootstrapStatistic.HodgesLehmann,
        bool resampleItems = true,
        int replicates = DefaultBootstrapReplicates,
        int seed = DefaultSeed)
    {
        var baseline = PrepareClusters(baselineClusters, nameof(baselineClusters));
        var treatment = PrepareClusters(treatmentClusters, nameof(treatmentClusters));
        RequireReplicates(replicates);
        if (baseline.Length == 0 || treatment.Length == 0) return null;

        var pooledBaseline = baseline.SelectMany(c => c).ToArray();
        var pooledTreatment = treatment.SelectMany(c => c).ToArray();
        double[]? scratch = null;
        double estimate = TwoSample(statistic, pooledBaseline, pooledBaseline.Length, pooledTreatment, pooledTreatment.Length, ref scratch);

        var rng = new Random(seed);
        var baselineBuffer = new double[ResampleCapacity(baseline)];
        var treatmentBuffer = new double[ResampleCapacity(treatment)];
        var values = new double[replicates];
        for (int b = 0; b < replicates; b++)
        {
            int nb = DrawClusters(baseline, rng, resampleItems, baselineBuffer);
            int nt = DrawClusters(treatment, rng, resampleItems, treatmentBuffer);
            values[b] = TwoSample(statistic, baselineBuffer, nb, treatmentBuffer, nt, ref scratch);
        }

        return new BootstrapDistribution(estimate, values, replicates, new[] { baseline.Length, treatment.Length });
    }

    // --- Strata ----------------------------------------------------------------------------------

    /// <summary>
    /// The time-of-week stratum of a UTC instant: six 4-hour UTC blocks (00–04 … 20–24) on a
    /// weekday or a weekend day (Saturday and Sunday UTC), twelve strata in all — the
    /// post-stratification cells of Cochran (1977), § 5A.9. A <see cref="DateTimeKind.Unspecified"/>
    /// value is read as UTC; a <see cref="DateTimeKind.Local"/> one is refused, because converting
    /// it would depend on the machine's time zone.
    /// </summary>
    public static ConsistencyStratum AssignStratum(DateTime utc)
    {
        if (utc.Kind == DateTimeKind.Local)
        {
            throw new ArgumentException("The instant must be UTC, not local time.", nameof(utc));
        }

        bool weekend = utc.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        int index = (weekend ? 6 : 0) + utc.Hour / 4;
        return new ConsistencyStratum(index, StratumLabel(index));
    }

    /// <summary>The label of stratum <paramref name="index"/>, for example "Weekend 20–24 UTC".</summary>
    public static string StratumLabel(int index)
    {
        if (index < 0 || index >= StratumCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "A stratum index lies between 0 and 11.");
        }

        int block = index % 6;
        string day = index < 6 ? "Weekday" : "Weekend";
        string start = (block * 4).ToString("00", CultureInfo.InvariantCulture);
        string end = (block * 4 + 4).ToString("00", CultureInfo.InvariantCulture);
        return day + " " + start + "–" + end + " UTC";
    }

    /// <summary>
    /// The stratified shift between two periods, treatment minus baseline, over common support
    /// only: each stratum present in both periods contributes its within-stratum shift with equal
    /// weight, and observations in strata present in one period only are excluded and their share
    /// reported (post-stratification, Cochran 1977, § 5A.9; common support, Heckman, Ichimura &amp;
    /// Todd 1997). Equal weights keep the comparison from tracking a change in when the traffic
    /// came.
    /// </summary>
    public static StratifiedShiftResult StratifiedShift(
        IReadOnlyList<StratifiedObservation> baseline,
        IReadOnlyList<StratifiedObservation> treatment,
        BootstrapStatistic statistic = BootstrapStatistic.HodgesLehmann)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(treatment);
        RequireFinite(baseline.Select(o => o.Value).ToArray(), nameof(baseline));
        RequireFinite(treatment.Select(o => o.Value).ToArray(), nameof(treatment));

        var b = baseline.GroupBy(o => o.Stratum).ToDictionary(g => g.Key, g => g.Select(o => o.Value).ToArray());
        var t = treatment.GroupBy(o => o.Stratum).ToDictionary(g => g.Key, g => g.Select(o => o.Value).ToArray());
        var common = b.Keys.Intersect(t.Keys).OrderBy(k => k).ToList();

        int total = baseline.Count + treatment.Count;
        int included = common.Sum(s => b[s].Length + t[s].Length);

        var shifts = new Dictionary<int, double>();
        double[]? scratch = null;
        foreach (int s in common)
        {
            shifts[s] = TwoSample(statistic, b[s], b[s].Length, t[s], t[s].Length, ref scratch);
        }

        return new StratifiedShiftResult
        {
            Shift = common.Count > 0 ? common.Average(s => shifts[s]) : null,
            StrataUsed = common,
            StratumShifts = shifts,
            TotalObservationCount = total,
            ExcludedObservationCount = total - included,
            ExcludedShare = total > 0 ? (total - included) / (double)total : 0.0
        };
    }

    // --- Quantiles and robust slopes -------------------------------------------------------------

    /// <summary>
    /// The Harrell–Davis quantile estimator: <c>Σ Wᵢ x₍ᵢ₎</c> with
    /// <c>Wᵢ = I_{i/n}(a, b) − I_{(i−1)/n}(a, b)</c>, <c>a = p(n+1)</c>, <c>b = (1−p)(n+1)</c>,
    /// where <c>I</c> is the regularized incomplete beta function (Harrell &amp; Davis 1982).
    /// Null over an empty sample.
    /// </summary>
    public static double? HarrellDavisQuantile(IReadOnlyList<double> sample, double p)
    {
        RequireFinite(sample, nameof(sample));
        RequireOpenProbability(p, nameof(p));
        if (sample.Count == 0) return null;

        var sorted = sample.OrderBy(v => v).ToArray();
        return WeightedSum(HarrellDavisWeights(sorted.Length, p), sorted);
    }

    /// <summary>
    /// The shift function of two independent samples: at each decile 0.1 … 0.9 the Harrell–Davis
    /// quantile of <paramref name="y"/> minus that of <paramref name="x"/>, each with a pointwise
    /// percentile bootstrap interval from resampling both samples independently
    /// (Doksum 1974; Rousselet, Pernet &amp; Wilcox 2017). The intervals are not adjusted for
    /// the nine deciles. Null when either sample is empty.
    /// </summary>
    public static IReadOnlyList<ShiftFunctionDecile>? ShiftFunction(
        IReadOnlyList<double> x,
        IReadOnlyList<double> y,
        double level = 0.95,
        int replicates = DefaultBootstrapReplicates,
        int seed = DefaultSeed)
    {
        RequireFinite(x, nameof(x));
        RequireFinite(y, nameof(y));
        RequireReplicates(replicates);
        if (!(level > 0.0 && level < 1.0))
        {
            throw new ArgumentOutOfRangeException(nameof(level), level, "The confidence level must lie strictly between 0 and 1.");
        }

        if (x.Count == 0 || y.Count == 0) return null;

        const int Deciles = 9;
        var xs = x.OrderBy(v => v).ToArray();
        var ys = y.OrderBy(v => v).ToArray();
        var probabilities = Enumerable.Range(1, Deciles).Select(d => d / 10.0).ToArray();
        var weightsX = probabilities.Select(p => HarrellDavisWeights(xs.Length, p)).ToArray();
        var weightsY = probabilities.Select(p => HarrellDavisWeights(ys.Length, p)).ToArray();

        var qx = weightsX.Select(w => WeightedSum(w, xs)).ToArray();
        var qy = weightsY.Select(w => WeightedSum(w, ys)).ToArray();

        var rng = new Random(seed);
        var bx = new double[xs.Length];
        var by = new double[ys.Length];
        var replicateDifferences = new double[Deciles][];
        for (int d = 0; d < Deciles; d++) replicateDifferences[d] = new double[replicates];

        for (int b = 0; b < replicates; b++)
        {
            for (int i = 0; i < bx.Length; i++) bx[i] = xs[rng.Next(xs.Length)];
            for (int i = 0; i < by.Length; i++) by[i] = ys[rng.Next(ys.Length)];
            Array.Sort(bx);
            Array.Sort(by);
            for (int d = 0; d < Deciles; d++)
            {
                replicateDifferences[d][b] = WeightedSum(weightsY[d], by) - WeightedSum(weightsX[d], bx);
            }
        }

        var rows = new List<ShiftFunctionDecile>(Deciles);
        for (int d = 0; d < Deciles; d++)
        {
            var distribution = new BootstrapDistribution(qy[d] - qx[d], replicateDifferences[d], replicates, new[] { xs.Length, ys.Length });
            var (lower, upper) = distribution.Interval(level);
            rows.Add(new ShiftFunctionDecile(probabilities[d], qx[d], qy[d], qy[d] - qx[d], lower, upper));
        }

        return rows;
    }

    /// <summary>
    /// The Theil–Sen slope: the median of the pairwise slopes <c>(yⱼ − yᵢ) / (xⱼ − xᵢ)</c> over
    /// pairs with distinct <c>x</c> (Theil 1950; Sen 1968). Null when fewer than two distinct
    /// <c>x</c> values exist.
    /// </summary>
    public static double? TheilSenSlope(IReadOnlyList<double> xs, IReadOnlyList<double> ys)
    {
        RequireFinite(xs, nameof(xs));
        RequireFinite(ys, nameof(ys));
        if (xs.Count != ys.Count)
        {
            throw new ArgumentException("The x and y series must have the same length.", nameof(ys));
        }

        var slopes = new List<double>();
        for (int i = 0; i < xs.Count; i++)
        {
            for (int j = i + 1; j < xs.Count; j++)
            {
                double dx = xs[j] - xs[i];
                if (dx == 0.0) continue;
                slopes.Add((ys[j] - ys[i]) / dx);
            }
        }

        return BenchmarkGroupStatistics.Median(slopes);
    }

    // --- Equivalence and verdicts ----------------------------------------------------------------

    /// <summary>
    /// The two one-sided tests procedure, read from its interval: equivalent within
    /// ±<paramref name="margin"/> at α iff the (1 − 2α) interval — 90 % for α = 0.05 — lies
    /// strictly inside (−margin, margin) (Schuirmann 1987).
    /// </summary>
    public static bool Tost(double ciLow90, double ciHigh90, double margin)
    {
        RequirePositiveMargin(margin);
        if (double.IsNaN(ciLow90) || double.IsNaN(ciHigh90)) return false;
        return ciLow90 > -margin && ciHigh90 < margin;
    }

    /// <summary>
    /// Lakens' four outcomes of a difference test combined with an equivalence test against the
    /// smallest effect size of interest <paramref name="margin"/> (Lakens, Scheel &amp; Isager
    /// 2018), checked in this order:
    /// <list type="number">
    /// <item><b>Changed</b>: <paramref name="adjustedPValue"/> (Holm-adjusted) &lt; α and the 95 %
    /// interval lies wholly beyond the margin on one side. A positive shift is
    /// <see cref="ConsistencyVerdict.ChangedImproved"/> when <paramref name="higherIsBetter"/>
    /// (quality, streaming rate) and <see cref="ConsistencyVerdict.ChangedDegraded"/> when lower is
    /// better (time, tokens, cost); a negative shift the reverse.</item>
    /// <item><see cref="ConsistencyVerdict.ChangedNegligible"/>: the 95 % interval excludes 0 but
    /// lies inside (−margin, margin).</item>
    /// <item><see cref="ConsistencyVerdict.Equivalent"/>: <see cref="Tost"/> on the 90 % interval.</item>
    /// <item>Otherwise <see cref="ConsistencyVerdict.Inconclusive"/>.</item>
    /// </list>
    /// </summary>
    public static ConsistencyVerdict Verdict(
        double adjustedPValue,
        double ciLow95,
        double ciHigh95,
        double ciLow90,
        double ciHigh90,
        double margin,
        bool higherIsBetter,
        double alpha = DefaultAlpha)
    {
        RequirePositiveMargin(margin);
        if (double.IsNaN(ciLow95) || double.IsNaN(ciHigh95)) return ConsistencyVerdict.Inconclusive;

        bool significant = !double.IsNaN(adjustedPValue) && adjustedPValue < alpha;
        if (significant && ciLow95 > margin)
        {
            return higherIsBetter ? ConsistencyVerdict.ChangedImproved : ConsistencyVerdict.ChangedDegraded;
        }

        if (significant && ciHigh95 < -margin)
        {
            return higherIsBetter ? ConsistencyVerdict.ChangedDegraded : ConsistencyVerdict.ChangedImproved;
        }

        bool excludesZero = ciLow95 > 0.0 || ciHigh95 < 0.0;
        bool insideMargin = ciLow95 > -margin && ciHigh95 < margin;
        if (excludesZero && insideMargin) return ConsistencyVerdict.ChangedNegligible;

        return Tost(ciLow90, ciHigh90, margin) ? ConsistencyVerdict.Equivalent : ConsistencyVerdict.Inconclusive;
    }

    /// <summary>
    /// <see cref="Verdict(double, double, double, double, double, double, bool, double)"/> with
    /// both intervals read from a bootstrap distribution (Lakens, Scheel &amp; Isager 2018).
    /// </summary>
    public static ConsistencyVerdict Verdict(
        double adjustedPValue,
        BootstrapDistribution distribution,
        double margin,
        bool higherIsBetter,
        double alpha = DefaultAlpha)
    {
        ArgumentNullException.ThrowIfNull(distribution);
        var ci95 = distribution.Interval95;
        var ci90 = distribution.Interval90;
        return Verdict(adjustedPValue, ci95.Lower, ci95.Upper, ci90.Lower, ci90.Upper, margin, higherIsBetter, alpha);
    }

    // --- Proportions and tests -------------------------------------------------------------------

    /// <summary>
    /// Fisher's exact test on the 2×2 table [[a, b], [c, d]], two-sided: the sum of the
    /// hypergeometric probabilities, at the observed margins, of every table no more probable
    /// than the observed one (Fisher 1935; Agresti 2002). Probabilities are compared with
    /// a relative tolerance of 1e-7 so that ties survive rounding.
    /// </summary>
    public static double FisherExactTwoSided(int a, int b, int c, int d)
    {
        if (a < 0 || b < 0 || c < 0 || d < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(a), "Table counts cannot be negative.");
        }

        int row1 = a + b;
        int row2 = c + d;
        int col1 = a + c;
        int n = row1 + row2;
        if (n == 0) return 1.0;

        var logFactorial = new double[n + 1];
        for (int k = 2; k <= n; k++) logFactorial[k] = logFactorial[k - 1] + Math.Log(k);

        double logDenominator = logFactorial[n] - logFactorial[col1] - logFactorial[n - col1];
        double LogProbability(int x) =>
            logFactorial[row1] - logFactorial[x] - logFactorial[row1 - x]
            + logFactorial[row2] - logFactorial[col1 - x] - logFactorial[row2 - col1 + x]
            - logDenominator;

        double observed = LogProbability(a);
        double threshold = observed + Math.Log(1.0 + FisherRelativeTolerance);
        int low = Math.Max(0, col1 - row2);
        int high = Math.Min(row1, col1);

        double sum = 0.0;
        for (int x = low; x <= high; x++)
        {
            double lp = LogProbability(x);
            if (lp <= threshold) sum += Math.Exp(lp);
        }

        return Math.Min(1.0, sum);
    }

    /// <summary>
    /// The 95 % Wilson score interval (Wilson 1927), from
    /// <see cref="BenchmarkProportionInterval.Wilson95"/>.
    /// </summary>
    public static (double Low, double High)? Wilson95(int k, int n) => BenchmarkProportionInterval.Wilson95(k, n);

    /// <summary>
    /// Holm's step-down adjustment (Holm 1979), from <see cref="BenchmarkBatteryStatistics.HolmAdjust"/>.
    /// </summary>
    public static IReadOnlyList<double> Holm(IReadOnlyList<double> pValues) => BenchmarkBatteryStatistics.HolmAdjust(pValues);

    /// <summary>
    /// The Benjamini–Hochberg step-up procedure (Benjamini &amp; Hochberg 1995), from
    /// <see cref="BenchmarkGroupStatistics.BenjaminiHochberg"/>.
    /// </summary>
    public static IReadOnlyList<BenchmarkFdrResult> BenjaminiHochberg(
        IReadOnlyList<double> pValues,
        double falseDiscoveryRate = BenchmarkGroupStatistics.DefaultFalseDiscoveryRate)
        => BenchmarkGroupStatistics.BenjaminiHochberg(pValues, falseDiscoveryRate);

    /// <summary>
    /// The Wilcoxon signed-rank test over paired differences (Wilcoxon 1945), from
    /// <see cref="BenchmarkGroupStatistics.WilcoxonSignedRank"/>.
    /// </summary>
    public static BenchmarkWilcoxonSignedRankResult WilcoxonSignedRank(IReadOnlyList<double> differences)
        => BenchmarkGroupStatistics.WilcoxonSignedRank(differences);

    /// <summary>
    /// The paired <i>t</i>-test over paired differences (Student 1908), from
    /// <see cref="BenchmarkGroupStatistics.PairedTTest"/>.
    /// </summary>
    public static BenchmarkPairedTTestResult PairedTTest(IReadOnlyList<double> differences)
        => BenchmarkGroupStatistics.PairedTTest(differences);

    /// <summary>
    /// Cohen's <i>d</i><sub>z</sub> for paired differences (Cohen 1988), from
    /// <see cref="BenchmarkGroupStatistics.CohensDz"/>.
    /// </summary>
    public static double? CohensDz(IReadOnlyList<double> differences) => BenchmarkGroupStatistics.CohensDz(differences);

    // --- Difference in differences ---------------------------------------------------------------

    /// <summary>
    /// The item difference-in-differences: per item, <c>(target after − target before) −
    /// (control after − control before)</c> on the cross-run item means of each cell, in the
    /// input's units (Angrist &amp; Pischke 2009, ch. 5). Only items present in all four cells
    /// count. The estimate is <paramref name="statistic"/> over the per-item differences.
    /// <para>The interval is a run-cluster bootstrap: each replicate resamples the runs of each of
    /// the four cells independently with replacement, recomputes the item means and the
    /// per-item differences, and with <paramref name="resampleItems"/> then resamples the items
    /// (Davison &amp; Hinkley 1997, § 3.8; Cameron, Gelbach &amp; Miller 2008; Bertrand, Duflo
    /// &amp; Mullainathan 2004). A replicate in which no item survives in all four cells is
    /// dropped.</para>
    /// <para>Null when a cell has no run or no item is common to all four cells.</para>
    /// </summary>
    public static ItemDifferenceInDifferencesResult? ItemDifferenceInDifferences(
        DifferenceInDifferencesArm target,
        DifferenceInDifferencesArm control,
        BootstrapStatistic statistic = BootstrapStatistic.HodgesLehmann,
        bool resampleItems = true,
        int replicates = DefaultBootstrapReplicates,
        int seed = DefaultSeed)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(control);
        RequireReplicates(replicates);

        var cells = new[] { target.Before, target.After, control.Before, control.After }
            .Select(runs => (runs ?? throw new ArgumentException("A period's run list cannot be null."))
                .Where(r => r != null)
                .ToArray())
            .ToArray();

        foreach (var cell in cells)
        {
            foreach (var run in cell)
            {
                RequireFinite(run.Values.ToArray(), nameof(target));
            }
        }

        if (cells.Any(c => c.Length == 0)) return null;

        var keys = cells
            .Select(c => new HashSet<string>(c.SelectMany(r => r.Keys), StringComparer.Ordinal))
            .Aggregate((acc, next) => { acc.IntersectWith(next); return acc; })
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();
        if (keys.Length == 0) return null;

        // matrices[cell][run][item], NaN where the run lacks the item.
        var matrices = cells
            .Select(c => c.Select(r => keys.Select(k => r.TryGetValue(k, out double v) ? v : double.NaN).ToArray()).ToArray())
            .ToArray();

        int itemCount = keys.Length;
        var means = new double[4][];
        for (int c = 0; c < 4; c++) means[c] = new double[itemCount];

        var allRuns = matrices.Select(m => Enumerable.Range(0, m.Length).ToArray()).ToArray();
        for (int c = 0; c < 4; c++) ItemMeans(matrices[c], allRuns[c], allRuns[c].Length, means[c]);

        var rows = new List<ItemDifferenceInDifferencesRow>(itemCount);
        for (int i = 0; i < itemCount; i++)
        {
            double targetChange = means[1][i] - means[0][i];
            double controlChange = means[3][i] - means[2][i];
            rows.Add(new ItemDifferenceInDifferencesRow(keys[i], targetChange, controlChange, targetChange - controlChange));
        }

        var pointDifferences = rows.Select(r => r.Difference).ToArray();
        double[]? scratch = null;
        double estimate = OneSample(statistic, pointDifferences, pointDifferences.Length, ref scratch);

        var rng = new Random(seed);
        var drawn = matrices.Select(m => new int[m.Length]).ToArray();
        var differences = new double[itemCount];
        var resampled = new double[itemCount];
        var values = new double[replicates];
        for (int b = 0; b < replicates; b++)
        {
            for (int c = 0; c < 4; c++)
            {
                for (int r = 0; r < drawn[c].Length; r++) drawn[c][r] = rng.Next(drawn[c].Length);
                ItemMeans(matrices[c], drawn[c], drawn[c].Length, means[c]);
            }

            int count = 0;
            for (int i = 0; i < itemCount; i++)
            {
                double value = (means[1][i] - means[0][i]) - (means[3][i] - means[2][i]);
                if (!double.IsNaN(value)) differences[count++] = value;
            }

            if (count == 0)
            {
                values[b] = double.NaN;
                continue;
            }

            if (resampleItems)
            {
                for (int i = 0; i < count; i++) resampled[i] = differences[rng.Next(count)];
                values[b] = OneSample(statistic, resampled, count, ref scratch);
            }
            else
            {
                values[b] = OneSample(statistic, differences, count, ref scratch);
            }
        }

        return new ItemDifferenceInDifferencesResult
        {
            Items = rows,
            Distribution = new BootstrapDistribution(estimate, values, replicates, cells.Select(c => c.Length).ToArray())
        };
    }

    // --- Change points ---------------------------------------------------------------------------

    /// <summary>
    /// Exploratory change-point detection for a change in mean of a normal series by PELT, the
    /// pruned exact optimal partitioning (Killick, Fearnhead &amp; Eckley 2012). The segment
    /// cost is the residual sum of squares over σ², σ estimated, unless
    /// <paramref name="noiseStandardDeviation"/> is given, as <c>1.4826 · MAD(Δx) / √2</c> from
    /// the first differences, so that the mean shifts themselves do not inflate it (Hampel 1974;
    /// Fryzlewicz 2014).
    /// <para>With <paramref name="penalty"/> null the penalty is MBIC (Zhang &amp; Siegmund 2007):
    /// <c>3 ln n</c> per change point plus <c>ln nᵢ</c> per segment, and pruning uses
    /// <c>K = −ln n</c>, which bounds the segment-length term so the pruning stays exact. A numeric
    /// penalty is a plain per-change penalty with <c>K = 0</c>.</para>
    /// <para>Each returned index is the position of the first value of a new segment, ascending.
    /// Empty when the series is shorter than two minimum segments or has no spread.</para>
    /// </summary>
    public static IReadOnlyList<int> Pelt(
        IReadOnlyList<double> series,
        double? penalty = null,
        int minimumSegmentLength = 2,
        double? noiseStandardDeviation = null)
    {
        RequireFinite(series, nameof(series));
        if (minimumSegmentLength < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumSegmentLength), minimumSegmentLength, "The minimum segment length must be at least 1.");
        }

        if (penalty.HasValue && (double.IsNaN(penalty.Value) || penalty.Value < 0.0))
        {
            throw new ArgumentOutOfRangeException(nameof(penalty), penalty, "The penalty cannot be negative.");
        }

        int n = series.Count;
        int m = minimumSegmentLength;
        if (n < 2 * m) return Array.Empty<int>();

        double sigma = noiseStandardDeviation ?? EstimateDifferenceNoise(series);
        if (!(sigma > 0.0) || double.IsInfinity(sigma)) return Array.Empty<int>();

        bool mbic = !penalty.HasValue;
        double beta = penalty ?? 3.0 * Math.Log(n);
        double pruning = mbic ? -Math.Log(n) : 0.0;

        var s1 = new double[n + 1];
        var s2 = new double[n + 1];
        for (int i = 0; i < n; i++)
        {
            double z = series[i] / sigma;
            s1[i + 1] = s1[i] + z;
            s2[i + 1] = s2[i] + z * z;
        }

        double Cost(int start, int end)
        {
            int length = end - start;
            double sum = s1[end] - s1[start];
            double sse = Math.Max(0.0, s2[end] - s2[start] - sum * sum / length);
            return mbic ? sse + Math.Log(length) : sse;
        }

        var f = new double[n + 1];
        var last = new int[n + 1];
        f[0] = -beta;
        var candidates = new List<int> { 0 };

        for (int t = m; t <= n; t++)
        {
            int fresh = t - m;
            if (fresh >= m) candidates.Add(fresh);

            double best = double.PositiveInfinity;
            int argBest = 0;
            foreach (int tau in candidates)
            {
                double value = f[tau] + Cost(tau, t) + beta;
                if (value < best)
                {
                    best = value;
                    argBest = tau;
                }
            }

            f[t] = best;
            last[t] = argBest;
            int end = t;
            candidates.RemoveAll(tau => f[tau] + Cost(tau, end) + pruning > f[end]);
        }

        var changePoints = new List<int>();
        int cursor = n;
        while (cursor > 0)
        {
            int previous = last[cursor];
            if (previous > 0) changePoints.Add(previous);
            cursor = previous;
        }

        changePoints.Reverse();
        return changePoints;
    }

    // --- Power and design ------------------------------------------------------------------------

    /// <summary>
    /// The minimum detectable effect of a two-sided comparison of two period means:
    /// <c>(z_{1−α/2} + z_{power}) · σ · √(1/n₁ + 1/n₂)</c>, which is
    /// <c>(z_{1−α/2} + z_{power}) · √2 · σ / √n</c> at equal counts (Cohen 1988, ch. 2;
    /// Bloom 1995).
    /// </summary>
    public static double MinimumDetectableEffect(
        double standardDeviation,
        int baselineCount,
        int treatmentCount,
        double alpha = DefaultAlpha,
        double power = DefaultPower)
    {
        if (double.IsNaN(standardDeviation) || standardDeviation < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(standardDeviation), standardDeviation, "The standard deviation cannot be negative.");
        }

        if (baselineCount < 1) throw new ArgumentOutOfRangeException(nameof(baselineCount), baselineCount, "The count must be at least 1.");
        if (treatmentCount < 1) throw new ArgumentOutOfRangeException(nameof(treatmentCount), treatmentCount, "The count must be at least 1.");
        RequireOpenProbability(alpha, nameof(alpha));
        RequireOpenProbability(power, nameof(power));

        double z = NormalQuantile(1.0 - alpha / 2.0) + NormalQuantile(power);
        return z * standardDeviation * Math.Sqrt(1.0 / baselineCount + 1.0 / treatmentCount);
    }

    /// <summary>
    /// The minimum detectable effect of two periods of runs, each run a list of item values
    /// (Cohen 1988, ch. 2; Bloom 1995). With at least two runs in each period, σ is the pooled
    /// run-to-run standard deviation of the run means — the reproducibility SD — and the counts
    /// are run counts. Otherwise σ is the pooled item standard deviation, the counts are item
    /// counts, and <see cref="MinimumDetectableEffectResult.CapNote"/> is set: with one run per
    /// period run-to-run noise is not estimable, so the effect is a floor, not a fair estimate.
    /// <para>Null when a period has no value or the pooled standard deviation is undefined.</para>
    /// </summary>
    public static MinimumDetectableEffectResult? MinimumDetectableEffect(
        IReadOnlyList<IReadOnlyList<double>> baselineRuns,
        IReadOnlyList<IReadOnlyList<double>> treatmentRuns,
        double alpha = DefaultAlpha,
        double power = DefaultPower)
    {
        var baseline = PrepareClusters(baselineRuns, nameof(baselineRuns));
        var treatment = PrepareClusters(treatmentRuns, nameof(treatmentRuns));
        if (baseline.Length == 0 || treatment.Length == 0) return null;

        bool perRun = baseline.Length >= 2 && treatment.Length >= 2;
        double[] b = perRun ? baseline.Select(r => r.Average()).ToArray() : baseline.SelectMany(r => r).ToArray();
        double[] t = perRun ? treatment.Select(r => r.Average()).ToArray() : treatment.SelectMany(r => r).ToArray();

        double? sd = PooledStandardDeviation(b, t);
        if (!sd.HasValue) return null;

        return new MinimumDetectableEffectResult
        {
            Effect = MinimumDetectableEffect(sd.Value, b.Length, t.Length, alpha, power),
            StandardDeviation = sd.Value,
            BaselineCount = b.Length,
            TreatmentCount = t.Length,
            CapNote = !perRun,
            Note = perRun ? null : MinimumDetectableEffectResult.OneRunPerPeriodNote,
            Alpha = alpha,
            Power = power
        };
    }

    /// <summary>
    /// Kish's design effect of cluster sampling: <c>1 + (m − 1) · ICC</c> for clusters of size
    /// <c>m</c> (Kish 1965).
    /// </summary>
    public static double DesignEffect(double clusterSize, double intraclassCorrelation)
    {
        if (double.IsNaN(clusterSize) || clusterSize < 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(clusterSize), clusterSize, "The cluster size must be at least 1.");
        }

        return 1.0 + (clusterSize - 1.0) * intraclassCorrelation;
    }

    /// <summary>
    /// The flip rate: the share of paired items whose correctness differs between the two windows
    /// (Dutta et al. 2024). Null over no pairs.
    /// </summary>
    public static double? FlipRate(IReadOnlyList<(bool Before, bool After)> pairs)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        if (pairs.Count == 0) return null;
        return pairs.Count(p => p.Before != p.After) / (double)pairs.Count;
    }

    /// <summary>
    /// The null flip rate: <see cref="FlipRate"/> over replicate pairs drawn inside the baseline,
    /// the rate at which correctness flips with nothing changed (Dutta et al. 2024). Null over no
    /// pairs.
    /// </summary>
    public static double? NullFlipRate(IReadOnlyList<(bool First, bool Second)> replicatePairs)
    {
        ArgumentNullException.ThrowIfNull(replicatePairs);
        return FlipRate(replicatePairs.Select(p => (Before: p.First, After: p.Second)).ToArray());
    }

    // --- Distributions ---------------------------------------------------------------------------

    /// <summary>
    /// The standard normal quantile by Acklam's rational approximation, relative error below
    /// 1.15e-9 (Acklam 2003). −∞ at 0 and +∞ at 1.
    /// </summary>
    public static double NormalQuantile(double p)
    {
        if (double.IsNaN(p) || p < 0.0 || p > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(p), p, "A probability lies between 0 and 1.");
        }

        if (p == 0.0) return double.NegativeInfinity;
        if (p == 1.0) return double.PositiveInfinity;

        const double A1 = -3.969683028665376e+01, A2 = 2.209460984245205e+02, A3 = -2.759285104469687e+02;
        const double A4 = 1.383577518672690e+02, A5 = -3.066479806614716e+01, A6 = 2.506628277459239e+00;
        const double B1 = -5.447609879822406e+01, B2 = 1.615858368580409e+02, B3 = -1.556989798598866e+02;
        const double B4 = 6.680131188771972e+01, B5 = -1.328068155288572e+01;
        const double C1 = -7.784894002430293e-03, C2 = -3.223964580411365e-01, C3 = -2.400758277161838e+00;
        const double C4 = -2.549732539343734e+00, C5 = 4.374664141464968e+00, C6 = 2.938163982698783e+00;
        const double D1 = 7.784695709041462e-03, D2 = 3.224671290700398e-01, D3 = 2.445134137142996e+00;
        const double D4 = 3.754408661907416e+00;
        const double PLow = 0.02425;
        const double PHigh = 1.0 - PLow;

        if (p < PLow)
        {
            double q = Math.Sqrt(-2.0 * Math.Log(p));
            return (((((C1 * q + C2) * q + C3) * q + C4) * q + C5) * q + C6)
                   / ((((D1 * q + D2) * q + D3) * q + D4) * q + 1.0);
        }

        if (p <= PHigh)
        {
            double q = p - 0.5;
            double r = q * q;
            return (((((A1 * r + A2) * r + A3) * r + A4) * r + A5) * r + A6) * q
                   / (((((B1 * r + B2) * r + B3) * r + B4) * r + B5) * r + 1.0);
        }

        double qUpper = Math.Sqrt(-2.0 * Math.Log(1.0 - p));
        return -(((((C1 * qUpper + C2) * qUpper + C3) * qUpper + C4) * qUpper + C5) * qUpper + C6)
               / ((((D1 * qUpper + D2) * qUpper + D3) * qUpper + D4) * qUpper + 1.0);
    }

    // --- Internals -------------------------------------------------------------------------------

    private static double OneSample(BootstrapStatistic statistic, double[] values, int count, ref double[]? scratch)
    {
        if (count == 0) return double.NaN;
        switch (statistic)
        {
            case BootstrapStatistic.HodgesLehmann:
                return OneSampleHodgesLehmann(values, count, ref scratch);
            case BootstrapStatistic.Mean:
                return Mean(values, count);
            case BootstrapStatistic.Median:
                EnsureCapacity(ref scratch, count);
                Array.Copy(values, scratch!, count);
                return MedianInPlace(scratch!, count);
            default:
                throw new ArgumentOutOfRangeException(nameof(statistic), statistic, "Unknown statistic.");
        }
    }

    private static double TwoSample(BootstrapStatistic statistic, double[] x, int nx, double[] y, int ny, ref double[]? scratch)
    {
        if (nx == 0 || ny == 0) return double.NaN;
        switch (statistic)
        {
            case BootstrapStatistic.HodgesLehmann:
                return TwoSampleHodgesLehmann(x, nx, y, ny, ref scratch);
            case BootstrapStatistic.Mean:
                return Mean(y, ny) - Mean(x, nx);
            case BootstrapStatistic.Median:
                EnsureCapacity(ref scratch, Math.Max(nx, ny));
                Array.Copy(y, scratch!, ny);
                double my = MedianInPlace(scratch!, ny);
                Array.Copy(x, scratch!, nx);
                double mx = MedianInPlace(scratch!, nx);
                return my - mx;
            default:
                throw new ArgumentOutOfRangeException(nameof(statistic), statistic, "Unknown statistic.");
        }
    }

    private static double OneSampleHodgesLehmann(double[] values, int count, ref double[]? scratch)
    {
        long pairs = (long)count * (count + 1) / 2;
        RequirePairwiseSize(pairs);
        int m = (int)pairs;
        EnsureCapacity(ref scratch, m);
        var walsh = scratch!;

        int k = 0;
        for (int i = 0; i < count; i++)
        {
            for (int j = i; j < count; j++)
            {
                walsh[k++] = 0.5 * (values[i] + values[j]);
            }
        }

        return MedianInPlace(walsh, m);
    }

    private static double TwoSampleHodgesLehmann(double[] x, int nx, double[] y, int ny, ref double[]? scratch)
    {
        long pairs = (long)nx * ny;
        RequirePairwiseSize(pairs);
        int m = (int)pairs;
        EnsureCapacity(ref scratch, m);
        var differences = scratch!;

        int k = 0;
        for (int i = 0; i < nx; i++)
        {
            for (int j = 0; j < ny; j++)
            {
                differences[k++] = y[j] - x[i];
            }
        }

        return MedianInPlace(differences, m);
    }

    private static double Mean(double[] values, int count)
    {
        double sum = 0.0;
        for (int i = 0; i < count; i++) sum += values[i];
        return sum / count;
    }

    /// <summary>The median of the first <paramref name="count"/> values, reordering them.</summary>
    private static double MedianInPlace(double[] values, int count)
    {
        int k = count / 2;
        double upper = SelectInPlace(values, count, k);
        if (count % 2 == 1) return upper;

        // After selection every value left of k is at most values[k].
        double lower = values[0];
        for (int i = 1; i < k; i++)
        {
            if (values[i] > lower) lower = values[i];
        }

        return 0.5 * (lower + upper);
    }

    /// <summary>
    /// Hoare's selection (quickselect) with a median-of-three pivot: returns the k-th smallest of
    /// the first <paramref name="count"/> values and leaves every value left of k at most it.
    /// </summary>
    private static double SelectInPlace(double[] a, int count, int k)
    {
        int lo = 0;
        int hi = count - 1;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (a[mid] < a[lo]) Swap(a, mid, lo);
            if (a[hi] < a[lo]) Swap(a, hi, lo);
            if (a[hi] < a[mid]) Swap(a, hi, mid);
            double pivot = a[mid];

            int i = lo;
            int j = hi;
            while (i <= j)
            {
                while (a[i] < pivot) i++;
                while (a[j] > pivot) j--;
                if (i <= j)
                {
                    Swap(a, i, j);
                    i++;
                    j--;
                }
            }

            if (k <= j) hi = j;
            else if (k >= i) lo = i;
            else return a[k];
        }

        return a[k];
    }

    private static void Swap(double[] a, int i, int j) => (a[i], a[j]) = (a[j], a[i]);

    private static double[] HarrellDavisWeights(int n, double p)
    {
        double a = p * (n + 1);
        double b = (1.0 - p) * (n + 1);
        var weights = new double[n];
        double previous = 0.0;
        for (int i = 1; i <= n; i++)
        {
            double current = i == n ? 1.0 : BenchmarkGroupStatistics.RegularizedIncompleteBeta(a, b, (double)i / n);
            weights[i - 1] = current - previous;
            previous = current;
        }

        return weights;
    }

    private static double WeightedSum(double[] weights, double[] sorted)
    {
        double sum = 0.0;
        for (int i = 0; i < weights.Length; i++) sum += weights[i] * sorted[i];
        return sum;
    }

    private static void ItemMeans(double[][] matrix, int[] runs, int runCount, double[] means)
    {
        int items = means.Length;
        for (int i = 0; i < items; i++)
        {
            double sum = 0.0;
            int n = 0;
            for (int r = 0; r < runCount; r++)
            {
                double v = matrix[runs[r]][i];
                if (double.IsNaN(v)) continue;
                sum += v;
                n++;
            }

            means[i] = n > 0 ? sum / n : double.NaN;
        }
    }

    private static int DrawClusters(double[][] clusters, Random rng, bool resampleItems, double[] buffer)
    {
        int count = 0;
        int k = clusters.Length;
        for (int j = 0; j < k; j++)
        {
            var cluster = clusters[rng.Next(k)];
            if (resampleItems)
            {
                for (int i = 0; i < cluster.Length; i++) buffer[count++] = cluster[rng.Next(cluster.Length)];
            }
            else
            {
                Array.Copy(cluster, 0, buffer, count, cluster.Length);
                count += cluster.Length;
            }
        }

        return count;
    }

    private static int ResampleCapacity(double[][] clusters) => clusters.Length * clusters.Max(c => c.Length);

    private static double[][] PrepareClusters(IReadOnlyList<IReadOnlyList<double>> clusters, string name)
    {
        ArgumentNullException.ThrowIfNull(clusters, name);
        var prepared = new List<double[]>(clusters.Count);
        foreach (var cluster in clusters)
        {
            if (cluster == null || cluster.Count == 0) continue;
            RequireFinite(cluster, name);
            prepared.Add(cluster.ToArray());
        }

        return prepared.ToArray();
    }

    private static double EstimateDifferenceNoise(IReadOnlyList<double> series)
    {
        var differences = new double[series.Count - 1];
        for (int i = 1; i < series.Count; i++) differences[i - 1] = series[i] - series[i - 1];

        double center = BenchmarkGroupStatistics.Median(differences)!.Value;
        double mad = BenchmarkGroupStatistics.Median(differences.Select(d => Math.Abs(d - center)).ToArray())!.Value;
        double sigma = 1.4826 * mad / Math.Sqrt(2.0);
        if (sigma > 0.0) return sigma;

        double? sd = BenchmarkGroupStatistics.SampleStandardDeviation(differences);
        return sd.HasValue ? sd.Value / Math.Sqrt(2.0) : 0.0;
    }

    private static double? PooledStandardDeviation(double[] a, double[] b)
    {
        double dfA = a.Length - 1;
        double dfB = b.Length - 1;
        double weighted = 0.0;
        double df = 0.0;
        if (dfA > 0)
        {
            weighted += dfA * BenchmarkGroupStatistics.SampleVariance(a)!.Value;
            df += dfA;
        }

        if (dfB > 0)
        {
            weighted += dfB * BenchmarkGroupStatistics.SampleVariance(b)!.Value;
            df += dfB;
        }

        return df > 0 ? Math.Sqrt(weighted / df) : null;
    }

    private static void EnsureCapacity(ref double[]? scratch, int size)
    {
        if (scratch == null || scratch.Length < size) scratch = new double[size];
    }

    private static void RequirePairwiseSize(long pairs)
    {
        if (pairs > MaxPairwiseCount)
        {
            throw new ArgumentException("The sample is too large for a pairwise Hodges-Lehmann estimate.");
        }
    }

    private static void RequireFinite(IReadOnlyList<double> values, string name)
    {
        ArgumentNullException.ThrowIfNull(values, name);
        for (int i = 0; i < values.Count; i++)
        {
            if (!double.IsFinite(values[i]))
            {
                throw new ArgumentException("Values must be finite.", name);
            }
        }
    }

    private static void RequireReplicates(int replicates)
    {
        if (replicates < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(replicates), replicates, "At least one bootstrap replicate is required.");
        }
    }

    private static void RequireOpenProbability(double p, string name)
    {
        if (!(p > 0.0 && p < 1.0))
        {
            throw new ArgumentOutOfRangeException(name, p, "The probability must lie strictly between 0 and 1.");
        }
    }

    private static void RequirePositiveMargin(double margin)
    {
        if (!(margin > 0.0) || double.IsInfinity(margin))
        {
            throw new ArgumentOutOfRangeException(nameof(margin), margin, "The equivalence margin must be positive and finite.");
        }
    }
}
