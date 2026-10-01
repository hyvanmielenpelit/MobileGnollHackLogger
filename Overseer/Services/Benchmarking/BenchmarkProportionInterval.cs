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
}
