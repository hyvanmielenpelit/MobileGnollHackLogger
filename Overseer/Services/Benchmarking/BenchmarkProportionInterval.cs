namespace Overseer.Services.Benchmarking;

using System;

/// <summary>Confidence intervals for a binomial proportion.</summary>
public static class BenchmarkProportionInterval
{
    /// <summary>The two-sided 95 % normal quantile.</summary>
    public const double Z95 = 1.959964;

    /// <summary>
    /// The 95 % Wilson score interval for <paramref name="k"/> successes in <paramref name="n"/>
    /// trials, clamped to [0, 1]. Null when <paramref name="n"/> is 0.
    /// </summary>
    public static (double Low, double High)? Wilson95(int k, int n)
    {
        if (n < 0) throw new ArgumentOutOfRangeException(nameof(n), n, "The trial count cannot be negative.");
        if (k < 0 || k > n) throw new ArgumentOutOfRangeException(nameof(k), k, "The success count must lie between 0 and the trial count.");
        if (n == 0) return null;

        double p = (double)k / n;
        double z2 = Z95 * Z95;
        double denominator = 1.0 + z2 / n;
        double center = (p + z2 / (2.0 * n)) / denominator;
        double halfWidth = Z95 * Math.Sqrt(p * (1.0 - p) / n + z2 / (4.0 * n * n)) / denominator;

        double low = k == 0 ? 0.0 : Math.Max(0.0, center - halfWidth);
        double high = k == n ? 1.0 : Math.Min(1.0, center + halfWidth);
        return (low, high);
    }

    /// <summary>
    /// The 95 % Wilson score interval for an effective sample of <paramref name="kEff"/> successes in
    /// <paramref name="nEff"/> trials, as a design-effect adjustment produces it; clamped to [0, 1].
    /// Null when <paramref name="nEff"/> is 0 or less. On integer inputs it equals
    /// <see cref="Wilson95(int, int)"/> exactly: <paramref name="kEff"/> 0 gives a lower bound of 0, and
    /// <paramref name="kEff"/> equal to <paramref name="nEff"/> an upper bound of 1.
    /// </summary>
    public static (double Low, double High)? Wilson95(double kEff, double nEff)
    {
        if (double.IsNaN(nEff) || double.IsInfinity(nEff)) throw new ArgumentOutOfRangeException(nameof(nEff), nEff, "The effective trial count must be finite.");
        if (nEff <= 0) return null;
        if (double.IsNaN(kEff) || kEff < 0 || kEff > nEff) throw new ArgumentOutOfRangeException(nameof(kEff), kEff, "The effective success count must lie between 0 and the effective trial count.");

        double p = kEff / nEff;
        double z2 = Z95 * Z95;
        double denominator = 1.0 + z2 / nEff;
        double center = (p + z2 / (2.0 * nEff)) / denominator;
        double halfWidth = Z95 * Math.Sqrt(p * (1.0 - p) / nEff + z2 / (4.0 * nEff * nEff)) / denominator;

        double low = kEff == 0 ? 0.0 : Math.Max(0.0, center - halfWidth);
        double high = kEff == nEff ? 1.0 : Math.Min(1.0, center + halfWidth);
        return (low, high);
    }
}
