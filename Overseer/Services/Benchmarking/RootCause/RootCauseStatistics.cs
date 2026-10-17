namespace Overseer.Services.Benchmarking.RootCause;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Spearman's rank correlation with its Fisher-z interval.</summary>
public sealed record RootCauseSpearman(int Count, double Rho, double Lower, double Upper)
{
    /// <summary>The 95% interval lies on one side of zero.</summary>
    public bool ExcludesZero => Lower > 0 || Upper < 0;
}

/// <summary>A Mann–Whitney rank-sum test of two independent groups.</summary>
public sealed record RootCauseMannWhitney(int FirstCount, int SecondCount, double U, double Z, double PValue, double MedianDifference);

/// <summary>A bootstrap percentile interval.</summary>
public sealed record RootCauseInterval(double Lower, double Upper)
{
    public bool ExcludesZero => Lower > 0 || Upper < 0;
}

/// <summary>
/// The statistics of the root-cause engine. Holm's adjustment, Wilson's interval, the normal
/// distribution and percentiles are the existing helpers (<see cref="BenchmarkBatteryStatistics.HolmAdjust"/>,
/// <see cref="BenchmarkProportionInterval.Wilson95"/>, <see cref="BenchmarkGroupStatistics.NormalCdf"/>,
/// <see cref="BenchmarkGroupStatistics.Percentile"/>); Spearman's ρ, the Mann–Whitney test and the
/// bootstrap are new here. Pure and deterministic: every bootstrap draws from <c>new Random(Seed)</c>.
/// </summary>
public static class RootCauseStatistics
{
    /// <summary>The bootstrap seed of every root-cause interval.</summary>
    public const int Seed = 20261015;

    /// <summary>Resamples behind every root-cause bootstrap interval.</summary>
    public const int Resamples = 2000;

    /// <summary>An adjusted p-value below this rejects a null.</summary>
    public const double SignificanceLevel = 0.05;

    /// <summary>Spearman's ρ needs at least this many pairs for its Fisher-z interval.</summary>
    public const int MinSpearmanCount = 4;

    /// <summary>
    /// Spearman's ρ of <paramref name="x"/> and <paramref name="y"/> (paired, average ranks for ties), with
    /// the 95% Fisher-z interval <c>tanh(atanh ρ ± 1.96 / √(n − 3))</c>. Null below
    /// <see cref="MinSpearmanCount"/> pairs or when either side has no spread. Used only across one
    /// model's questions, never across models.
    /// </summary>
    public static RootCauseSpearman? Spearman(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        if (x.Count != y.Count) throw new ArgumentException("Spearman's ρ needs paired values.", nameof(y));

        int n = x.Count;
        if (n < MinSpearmanCount) return null;

        var rx = Ranks(x);
        var ry = Ranks(y);
        double mx = rx.Average();
        double my = ry.Average();
        double sxy = 0, sxx = 0, syy = 0;
        for (int i = 0; i < n; i++)
        {
            sxy += (rx[i] - mx) * (ry[i] - my);
            sxx += (rx[i] - mx) * (rx[i] - mx);
            syy += (ry[i] - my) * (ry[i] - my);
        }
        if (sxx <= 0 || syy <= 0) return null;

        double rho = Math.Clamp(sxy / Math.Sqrt(sxx * syy), -1.0, 1.0);
        double z = Math.Atanh(Math.Clamp(rho, -0.999999, 0.999999));
        double half = 1.959963984540054 / Math.Sqrt(n - 3);
        return new RootCauseSpearman(n, rho, Math.Tanh(z - half), Math.Tanh(z + half));
    }

    /// <summary>
    /// The two-sided Mann–Whitney test of <paramref name="first"/> against <paramref name="second"/>: the
    /// normal approximation with the tie correction and a continuity correction. <c>U</c> counts the
    /// first group's wins. Null when either group is empty or every value is tied.
    /// </summary>
    public static RootCauseMannWhitney? MannWhitney(IReadOnlyList<double> first, IReadOnlyList<double> second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        int n1 = first.Count, n2 = second.Count;
        if (n1 == 0 || n2 == 0) return null;

        var all = first.Concat(second).ToList();
        int n = all.Count;
        var ranks = Ranks(all);
        double r1 = 0;
        for (int i = 0; i < n1; i++) r1 += ranks[i];
        double u = r1 - n1 * (n1 + 1) / 2.0;

        double ties = all.GroupBy(v => v).Select(g => (double)g.Count()).Where(t => t > 1).Sum(t => t * t * t - t);
        double variance = n1 * (double)n2 / 12.0 * ((n + 1) - ties / (n * (double)(n - 1)));
        if (variance <= 0) return null;

        double mean = n1 * (double)n2 / 2.0;
        double diff = u - mean;
        double corrected = Math.Abs(diff) <= 0.5 ? 0.0 : diff - Math.Sign(diff) * 0.5;
        double z = corrected / Math.Sqrt(variance);
        double p = Math.Clamp(2.0 * (1.0 - BenchmarkGroupStatistics.NormalCdf(Math.Abs(z))), 0.0, 1.0);

        double median1 = BenchmarkGroupStatistics.Median(first) ?? 0;
        double median2 = BenchmarkGroupStatistics.Median(second) ?? 0;
        return new RootCauseMannWhitney(n1, n2, u, z, p, median1 - median2);
    }

    /// <summary>Holm's step-down adjustment, results in input order (<see cref="BenchmarkBatteryStatistics.HolmAdjust"/>).</summary>
    public static IReadOnlyList<double> Holm(IReadOnlyList<double> pValues) => BenchmarkBatteryStatistics.HolmAdjust(pValues);

    /// <summary>The 95% Wilson interval of <paramref name="k"/> in <paramref name="n"/> (<see cref="BenchmarkProportionInterval.Wilson95"/>).</summary>
    public static (double Low, double High)? Wilson(int k, int n) => BenchmarkProportionInterval.Wilson95(k, n);

    /// <summary>
    /// The 95% percentile interval of <paramref name="statistic"/> over <see cref="Resamples"/> resamples
    /// of <paramref name="count"/> indices with replacement, drawn from <c>new Random(Seed)</c>. A resample
    /// for which the statistic is null or not finite is left out; null when fewer than half remain or
    /// <paramref name="count"/> is zero.
    /// </summary>
    public static RootCauseInterval? Bootstrap(int count, Func<int[], double?> statistic)
    {
        var values = BootstrapValues(count, 1, indices => statistic(indices) is double v ? new[] { v } : null);
        return values.Count == 1 ? values[0] : null;
    }

    /// <summary>
    /// <see cref="Bootstrap(int, Func{int[], double?})"/> for <paramref name="width"/> statistics computed
    /// from the same resamples: one interval per position, each null when fewer than half its values remain.
    /// </summary>
    public static IReadOnlyList<RootCauseInterval?> Bootstrap(int count, int width, Func<int[], double?[]?> statistics)
    {
        ArgumentNullException.ThrowIfNull(statistics);
        var collected = new List<double>[width];
        for (int k = 0; k < width; k++) collected[k] = new List<double>(Resamples);

        if (count > 0)
        {
            var random = new Random(Seed);
            var indices = new int[count];
            for (int b = 0; b < Resamples; b++)
            {
                for (int i = 0; i < count; i++) indices[i] = random.Next(count);
                var values = statistics(indices);
                if (values == null) continue;
                for (int k = 0; k < width && k < values.Length; k++)
                {
                    if (values[k] is double v && double.IsFinite(v)) collected[k].Add(v);
                }
            }
        }

        var result = new RootCauseInterval?[width];
        for (int k = 0; k < width; k++)
        {
            var list = collected[k];
            if (count == 0 || list.Count < Resamples / 2)
            {
                result[k] = null;
                continue;
            }
            result[k] = new RootCauseInterval(
                BenchmarkGroupStatistics.Percentile(list, 2.5)!.Value,
                BenchmarkGroupStatistics.Percentile(list, 97.5)!.Value);
        }
        return result;
    }

    private static List<RootCauseInterval?> BootstrapValues(int count, int width, Func<int[], double[]?> statistics)
        => Bootstrap(count, width, indices => statistics(indices)?.Select(v => (double?)v).ToArray()).ToList();

    /// <summary>The mean of a paired-bootstrap resample of <paramref name="values"/>, with its 95% percentile interval.</summary>
    public static (double Mean, RootCauseInterval? Interval) MeanWithInterval(IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0) return (0, null);
        double mean = values.Average();
        var interval = Bootstrap(values.Count, indices => indices.Average(i => values[i]));
        return (mean, interval);
    }

    /// <summary>
    /// The logarithmic mean <c>L(a, b) = (a − b) / (ln a − ln b)</c>, with <c>L(a, a) = a</c>; zero when
    /// either side is not positive.
    /// </summary>
    public static double LogMean(double a, double b)
    {
        if (a <= 0 || b <= 0) return 0;
        if (Math.Abs(a - b) <= 1e-12 * Math.Max(a, b)) return a;
        return (a - b) / (Math.Log(a) - Math.Log(b));
    }

    /// <summary>
    /// The LMDI split of <c>total = volume × price</c> between two sides into a volume effect and a price
    /// effect that sum exactly to <c>totalFirst − totalSecond</c>: the volume effect is
    /// <c>L(total₁, total₂) · ln(volume₁ / volume₂)</c> and the price effect the remainder. A side whose
    /// total, volume or price is not positive sends the whole difference to volume.
    /// </summary>
    public static (double Volume, double Price) Lmdi(double totalFirst, double totalSecond, double volumeFirst, double volumeSecond)
    {
        double delta = totalFirst - totalSecond;
        if (totalFirst <= 0 || totalSecond <= 0 || volumeFirst <= 0 || volumeSecond <= 0) return (delta, 0);
        double volume = LogMean(totalFirst, totalSecond) * Math.Log(volumeFirst / volumeSecond);
        return (volume, delta - volume);
    }

    /// <summary>
    /// The LMDI split of <c>total = volume × list price × caching factor</c> between two sides into a volume, a list-price
    /// and a caching effect that sum exactly to <c>totalFirst − totalSecond</c>: with <c>L = L(total₁, total₂)</c>, the
    /// volume effect is <c>L · ln(volume₁ / volume₂)</c>, the list-price effect <c>L · ln(list₁ / list₂)</c> and the caching
    /// effect the remainder, what sets each side's effective price apart from its list price. A side whose total, volume
    /// or list price is not positive falls back to <see cref="Lmdi"/>: its price effect becomes the list-price effect and
    /// the caching effect is 0.
    /// </summary>
    public static (double Volume, double List, double Caching) Lmdi3(
        double totalFirst, double totalSecond, double volumeFirst, double volumeSecond, double listFirst, double listSecond)
    {
        if (totalFirst <= 0 || totalSecond <= 0 || volumeFirst <= 0 || volumeSecond <= 0 || listFirst <= 0 || listSecond <= 0)
        {
            var (v, p) = Lmdi(totalFirst, totalSecond, volumeFirst, volumeSecond);
            return (v, p, 0);
        }
        double l = LogMean(totalFirst, totalSecond);
        double volume = l * Math.Log(volumeFirst / volumeSecond);
        double list = l * Math.Log(listFirst / listSecond);
        return (volume, list, totalFirst - totalSecond - volume - list);
    }

    /// <summary>Average ranks from 1, ties sharing the mean of their ranks.</summary>
    public static double[] Ranks(IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        int n = values.Count;
        var order = Enumerable.Range(0, n).OrderBy(i => values[i]).ThenBy(i => i).ToArray();
        var ranks = new double[n];
        int start = 0;
        while (start < n)
        {
            int end = start;
            while (end + 1 < n && values[order[end + 1]].Equals(values[order[start]])) end++;
            double rank = (start + end) / 2.0 + 1.0;
            for (int k = start; k <= end; k++) ranks[order[k]] = rank;
            start = end + 1;
        }
        return ranks;
    }

    /// <summary>The median, null for none (<see cref="BenchmarkGroupStatistics.Median"/>).</summary>
    public static double? Median(IReadOnlyList<double> values) => BenchmarkGroupStatistics.Median(values);
}
