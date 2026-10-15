namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// The GnollBench provider availability calculator: the cluster-adjusted Wilson interval, the summary
/// of a pool of runs, re-runs needed, the 5-minute bins and the formatting the client mirrors.
/// </summary>
public class BenchmarkProviderAvailabilityTests
{
    /// <summary>
    /// The formatting parity fixture. The Angular spec <c>provider-availability.spec.ts</c> copies this
    /// table verbatim; the interval inputs are <see cref="BenchmarkProportionInterval.Wilson95(int, int)"/>
    /// of (K, N). Change both together.
    /// </summary>
    public static readonly (int K, int N, string Rate, string Count, string Interval, string? OneIn, string Headline)[] ParityTable =
    {
        (0, 1480, "0%", "0 of 1,480 attempts", "95% CI 0–0.26%", null, "0% (0 of 1,480 attempts; 95% CI 0–0.26%)"),
        (12, 1480, "0.81%", "12 of 1,480 attempts", "95% CI 0.46–1.4%", "about 1 in 123", "0.81% (12 of 1,480 attempts; 95% CI 0.46–1.4%)"),
        (1, 37, "2.7%", "1 of 37 attempts", "95% CI 0.48–14%", null, "2.7% (1 of 37 attempts; 95% CI 0.48–14%; few attempts)"),
        (150, 1200, "13%", "150 of 1,200 attempts", "95% CI 11–14%", null, "13% (150 of 1,200 attempts; 95% CI 11–14%)"),
        (3, 99, "3.0%", "3 of 99 attempts", "95% CI 1.0–8.5%", null, "3.0% (3 of 99 attempts; 95% CI 1.0–8.5%; few attempts)"),
        (1, 1480, "0.068%", "1 of 1,480 attempts", "95% CI 0.012–0.38%", "about 1 in 1,480", "0.068% (1 of 1,480 attempts; 95% CI 0.012–0.38%)"),
        (1, 1, "100%", "1 of 1 attempt", "95% CI 21–100%", null, "100% (1 of 1 attempt; 95% CI 21–100%; few attempts)"),
    };

    public static IEnumerable<object[]> ParityRows => Enumerable.Range(0, ParityTable.Length).Select(i => new object[] { i });

    private static readonly DateTime T0 = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);

    private static ProviderAttemptRow Attempt(
        long runId,
        ProviderAttemptOutcome outcome,
        DateTime at,
        int callIndex = 0,
        int attemptIndex = 0,
        long? answerId = 1,
        ModelCallSource source = ModelCallSource.BenchmarkCandidate,
        int pass = 0) =>
        new(runId, answerId, source, pass, callIndex, attemptIndex, at, 1000, outcome, null, null, false);

    /// <summary><paramref name="total"/> single-attempt calls in one 10-minute block, the first <paramref name="errors"/> of them overloaded.</summary>
    private static IEnumerable<ProviderAttemptRow> Block(long runId, int block, int errors, int total, int callBase = 0)
    {
        var start = T0.AddMinutes(block * 10 + 1);
        for (int i = 0; i < total; i++)
        {
            var outcome = i < errors ? ProviderAttemptOutcome.ProviderOverloaded : ProviderAttemptOutcome.Ok;
            yield return Attempt(runId, outcome, start.AddSeconds(i), callIndex: callBase + i);
        }
    }

    private static RunLineageRow Run(
        long id,
        bool selected = true,
        bool recorded = true,
        long? replaces = null,
        BenchmarkReplacementCause? cause = null,
        int repairs = 0,
        int providerRepairs = 0,
        int lost = 0) =>
        new(id, selected, recorded, replaces, cause, repairs, providerRepairs, lost);

    // --- Formatting ---------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(ParityRows))]
    public void ParityTable_FormatsEveryRow(int index)
    {
        var row = ParityTable[index];
        var interval = BenchmarkProportionInterval.Wilson95(row.K, row.N)!.Value;

        Assert.Equal(row.Rate, BenchmarkProviderAvailability.FormatPercent((double)row.K / row.N));
        Assert.Equal(row.Count, BenchmarkProviderAvailability.FormatCount(row.K, row.N));
        Assert.Equal(row.Interval, BenchmarkProviderAvailability.FormatInterval(interval.Low, interval.High));
        Assert.Equal(row.OneIn, BenchmarkProviderAvailability.FormatOneIn(row.K, row.N));
        Assert.Equal(row.Headline, BenchmarkProviderAvailability.FormatRate(row.K, row.N, interval.Low, interval.High));
    }

    [Theory]
    [InlineData(0.0, "0%")]
    [InlineData(1.0, "100%")]
    [InlineData(0.0081081, "0.81%")]
    [InlineData(0.031, "3.1%")]
    [InlineData(0.12, "12%")]
    [InlineData(0.125, "13%")]
    [InlineData(0.5, "50%")]
    [InlineData(0.005, "0.50%")]
    [InlineData(0.000042, "0.0042%")]
    [InlineData(0.0999, "10%")]
    [InlineData(0.00996, "1.0%")]
    [InlineData(-0.1, "0%")]
    [InlineData(1.5, "100%")]
    public void FormatPercent_KeepsTwoSignificantDigits(double proportion, string expected)
    {
        Assert.Equal(expected, BenchmarkProviderAvailability.FormatPercent(proportion));
    }

    [Fact]
    public void FormatPercent_RejectsNaN()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BenchmarkProviderAvailability.FormatPercent(double.NaN));
    }

    [Theory]
    [InlineData(1, 1, "1 of 1 attempt")]
    [InlineData(0, 0, "0 of 0 attempts")]
    [InlineData(1234567, 2000000, "1,234,567 of 2,000,000 attempts")]
    public void FormatCount_UsesThousandsSeparatorAndSingular(int k, int n, string expected)
    {
        Assert.Equal(expected, BenchmarkProviderAvailability.FormatCount(k, n));
    }

    [Fact]
    public void FormatInterval_IsEmptyWithoutBothBounds()
    {
        Assert.Equal(string.Empty, BenchmarkProviderAvailability.FormatInterval(null, null));
        Assert.Equal(string.Empty, BenchmarkProviderAvailability.FormatInterval(0.1, null));
        Assert.Equal(string.Empty, BenchmarkProviderAvailability.FormatInterval(null, 0.1));
    }

    [Theory]
    [InlineData(0, 100, null)]
    [InlineData(1, 100, null)]
    [InlineData(1, 101, "about 1 in 101")]
    [InlineData(3, 1000, "about 1 in 333")]
    [InlineData(2, 5000, "about 1 in 2,500")]
    [InlineData(2, 5, null)]
    [InlineData(1, 0, null)]
    public void FormatOneIn_OnlyBelowOnePercent(int k, int n, string? expected)
    {
        Assert.Equal(expected, BenchmarkProviderAvailability.FormatOneIn(k, n));
    }

    [Fact]
    public void FormatRate_WithoutIntervalOrAttempts()
    {
        Assert.Equal("no valid attempts", BenchmarkProviderAvailability.FormatRate(0, 0, null, null));
        Assert.Equal("2.5% (5 of 200 attempts)", BenchmarkProviderAvailability.FormatRate(5, 200, null, null));
        Assert.Equal("2.0% (1 of 50 attempts; few attempts)", BenchmarkProviderAvailability.FormatRate(1, 50, null, null));
    }

    // --- Wilson on an effective sample ----------------------------------------------------------------

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(0, 1480)]
    [InlineData(12, 1480)]
    [InlineData(1, 37)]
    [InlineData(150, 1200)]
    [InlineData(3, 99)]
    [InlineData(5, 10)]
    [InlineData(18, 18)]
    public void Wilson95Double_EqualsTheIntegerVersionOnIntegers(int k, int n)
    {
        Assert.Equal(BenchmarkProportionInterval.Wilson95(k, n), BenchmarkProportionInterval.Wilson95((double)k, (double)n));
    }

    [Fact]
    public void Wilson95Double_HandlesBoundsAndFractions()
    {
        Assert.Null(BenchmarkProportionInterval.Wilson95(0.0, 0.0));
        Assert.Null(BenchmarkProportionInterval.Wilson95(0.0, -1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => BenchmarkProportionInterval.Wilson95(2.5, 2.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => BenchmarkProportionInterval.Wilson95(-0.1, 2.0));

        Assert.Equal(0.0, BenchmarkProportionInterval.Wilson95(0.0, 12.5)!.Value.Low);
        Assert.Equal(1.0, BenchmarkProportionInterval.Wilson95(12.5, 12.5)!.Value.High);

        var fractional = BenchmarkProportionInterval.Wilson95(0.99, 99.0)!.Value;
        Assert.True(fractional.Low > 0.0);
        Assert.True(fractional.Low < 0.01 && 0.01 < fractional.High);
        Assert.True(fractional.High < 1.0);
    }

    // --- Cluster-adjusted interval --------------------------------------------------------------------

    [Fact]
    public void ClusterAdjusted_SpreadErrorsHaveNoDesignEffect()
    {
        var clusters = Enumerable.Range(0, 10).Select(i => ($"b{i}", 1, 100));

        var (low, high, deff) = BenchmarkProviderAvailability.ClusterAdjustedWilson95(clusters);

        var plain = BenchmarkProportionInterval.Wilson95(10, 1000)!.Value;
        Assert.Equal(1.0, deff);
        Assert.Equal(plain.Low, low);
        Assert.Equal(plain.High, high);
    }

    [Fact]
    public void ClusterAdjusted_BurstInOneBlockWidensTheInterval()
    {
        var clusters = Enumerable.Range(0, 10).Select(i => ($"b{i}", i == 0 ? 10 : 0, 100));

        var (low, high, deff) = BenchmarkProviderAvailability.ClusterAdjustedWilson95(clusters);

        // V_cl = 10/9 · (9² + 9 · 1²) / 1000² = 1e-4; V_srs = 0.01 · 0.99 / 1000 = 9.9e-6.
        Assert.Equal(1e-4 / 9.9e-6, deff, 6);
        var plain = BenchmarkProportionInterval.Wilson95(10, 1000)!.Value;
        Assert.True(low < plain.Low);
        Assert.True(high > plain.High);
    }

    [Fact]
    public void ClusterAdjusted_ZeroErrorsGiveThePlainWilsonUpperBound()
    {
        var clusters = new[] { ("a", 0, 500), ("b", 0, 500), ("c", 0, 480) };

        var (low, high, deff) = BenchmarkProviderAvailability.ClusterAdjustedWilson95(clusters);

        Assert.Equal(1.0, deff);
        Assert.Equal(0.0, low);
        Assert.Equal(BenchmarkProportionInterval.Wilson95(0, 1480)!.Value.High, high);
        double ruleOfThree = 3.84 / (1480 + 3.84);
        Assert.InRange(high!.Value, ruleOfThree * 0.999, ruleOfThree * 1.001);
    }

    [Fact]
    public void ClusterAdjusted_NoValidAttemptsGivesNoInterval()
    {
        (double? Low, double? High, double Deff) none = (null, null, 1.0);
        Assert.Equal(none, BenchmarkProviderAvailability.ClusterAdjustedWilson95(Array.Empty<(string, int, int)>()));
        Assert.Equal(none, BenchmarkProviderAvailability.ClusterAdjustedWilson95(new[] { ("a", 0, 0) }));
    }

    [Fact]
    public void ClusterAdjusted_SumsEntriesOfOneClusterAndIgnoresEmptyOnes()
    {
        var split = BenchmarkProviderAvailability.ClusterAdjustedWilson95(new[] { ("a", 3, 50), ("a", 0, 50), ("b", 0, 100), ("c", 0, 0) });
        var merged = BenchmarkProviderAvailability.ClusterAdjustedWilson95(new[] { ("a", 3, 100), ("b", 0, 100) });

        Assert.Equal(merged, split);
        Assert.True(merged.Deff > 1.0);
    }

    [Fact]
    public void ClusterAdjusted_OneClusterHasNoDesignEffect()
    {
        var (low, high, deff) = BenchmarkProviderAvailability.ClusterAdjustedWilson95(new[] { ("a", 7, 100) });

        var plain = BenchmarkProportionInterval.Wilson95(7, 100)!.Value;
        Assert.Equal(1.0, deff);
        Assert.Equal(plain.Low, low);
        Assert.Equal(plain.High, high);
    }

    // --- Summary ----------------------------------------------------------------------------------------

    [Fact]
    public void Summarize_CountsCandidateAttemptsByClass()
    {
        var rows = new List<ProviderAttemptRow>
        {
            Attempt(1, ProviderAttemptOutcome.ProviderOverloaded, T0, callIndex: 0, attemptIndex: 0),
            Attempt(1, ProviderAttemptOutcome.Ok, T0.AddSeconds(1), callIndex: 0, attemptIndex: 1),
            Attempt(1, ProviderAttemptOutcome.Ok, T0.AddSeconds(2), callIndex: 1, attemptIndex: 0),
            Attempt(1, ProviderAttemptOutcome.OurThrottle, T0.AddSeconds(3), callIndex: 2, attemptIndex: 0),
            Attempt(1, ProviderAttemptOutcome.Ok, T0.AddSeconds(4), callIndex: 2, attemptIndex: 1),
            Attempt(1, ProviderAttemptOutcome.OurCanceled, T0.AddSeconds(5), callIndex: 3, attemptIndex: 0),
            Attempt(1, ProviderAttemptOutcome.RateLimitUnattributed, T0.AddSeconds(6), callIndex: 4, attemptIndex: 0),
            Attempt(1, ProviderAttemptOutcome.ProviderServerError, T0.AddSeconds(7), callIndex: 4, attemptIndex: 1),
            Attempt(1, ProviderAttemptOutcome.Ok, T0.AddSeconds(8), callIndex: 4, attemptIndex: 2),
            Attempt(1, ProviderAttemptOutcome.Ok, T0.AddSeconds(9), source: ModelCallSource.BenchmarkGrader),
            Attempt(1, ProviderAttemptOutcome.ProviderTimeout, T0.AddSeconds(10), callIndex: 1, source: ModelCallSource.BenchmarkGrader),
            Attempt(1, ProviderAttemptOutcome.OurKey, T0.AddSeconds(11), callIndex: 2, source: ModelCallSource.BenchmarkGrader),
        };

        var summary = BenchmarkProviderAvailability.Summarize(rows, new[] { Run(1, lost: 2) });

        Assert.True(summary.Recorded);
        Assert.Equal(1, summary.RecordedRuns);
        Assert.Equal(1, summary.TotalRuns);
        Assert.Equal(0, summary.ReplacedRunsIncluded);

        Assert.Equal(7, summary.ValidAttempts);
        Assert.Equal(2, summary.ProviderErrors);
        Assert.Equal(1, summary.UnattributedErrors);
        Assert.Equal(2, summary.OurSideFailures);
        Assert.Equal(1, summary.Overloaded);
        Assert.Equal(1, summary.ServerErrors);
        Assert.Equal(0, summary.Timeouts);
        Assert.Equal(0, summary.StreamFailures);
        Assert.Equal(0, summary.RateLimitedOurs);
        Assert.Equal(1, summary.RateLimitedUnattributed);
        Assert.Equal(0, summary.TransportUnattributed);

        Assert.Equal(Enum.GetValues<ProviderAttemptOutcome>().Length, summary.CountsByOutcome.Count);
        Assert.Equal(4, summary.CountsByOutcome[ProviderAttemptOutcome.Ok]);
        Assert.Equal(1, summary.CountsByOutcome[ProviderAttemptOutcome.OurThrottle]);
        Assert.Equal(0, summary.CountsByOutcome[ProviderAttemptOutcome.OurNetwork]);

        Assert.Equal(2.0 / 7, summary.ProviderErrorRate);
        Assert.Equal(3.0 / 7, summary.InclusiveRate);
        var plain = BenchmarkProportionInterval.Wilson95(2, 7)!.Value;
        Assert.Equal(1.0, summary.DesignEffect);
        Assert.Equal(plain.Low, summary.ProviderErrorRateLow);
        Assert.Equal(plain.High, summary.ProviderErrorRateHigh);
        Assert.True(summary.InclusiveRateHigh > summary.ProviderErrorRateHigh);
        Assert.True(summary.FewAttempts);

        Assert.Equal(4, summary.Calls);
        Assert.Equal(1, summary.FirstAttemptSuccessCalls);
        Assert.Equal(2, summary.QuestionsLost);

        Assert.Equal(2, summary.GraderValidAttempts);
        Assert.Equal(1, summary.GraderProviderErrors);
    }

    [Fact]
    public void Summarize_ExcludesUnrecordedAndUnknownRuns()
    {
        var rows = Block(1, 0, 0, 3)
            .Concat(Block(2, 0, 5, 5))
            .Concat(Block(3, 0, 5, 5))
            .ToList();

        var summary = BenchmarkProviderAvailability.Summarize(rows, new[] { Run(1, lost: 2), Run(2, recorded: false, lost: 7) });

        Assert.True(summary.Recorded);
        Assert.Equal(1, summary.RecordedRuns);
        Assert.Equal(2, summary.TotalRuns);
        Assert.Equal(3, summary.ValidAttempts);
        Assert.Equal(0, summary.ProviderErrors);
        Assert.Equal(2, summary.QuestionsLost);
    }

    [Fact]
    public void Summarize_PoolWithoutRecordedRunIsNotRecorded()
    {
        var summary = BenchmarkProviderAvailability.Summarize(Block(1, 0, 2, 10).ToList(), new[] { Run(1, recorded: false, repairs: 1) });

        Assert.False(summary.Recorded);
        Assert.Equal(1, summary.TotalRuns);
        Assert.Equal(0, summary.RecordedRuns);
        Assert.Equal(0, summary.ValidAttempts);
        Assert.Null(summary.ProviderErrorRate);
        Assert.Null(summary.ProviderErrorRateHigh);
        Assert.False(summary.FewAttempts);
        Assert.Empty(summary.Bins);
        Assert.Equal(1, summary.RepairPasses);
    }

    [Fact]
    public void Summarize_RecordedRunWithoutAttemptsHasNoRate()
    {
        var summary = BenchmarkProviderAvailability.Summarize(Array.Empty<ProviderAttemptRow>(), new[] { Run(1) });

        Assert.True(summary.Recorded);
        Assert.Equal(0, summary.ValidAttempts);
        Assert.Null(summary.ProviderErrorRate);
        Assert.Null(summary.ProviderErrorRateLow);
        Assert.Null(summary.InclusiveRate);
        Assert.Equal(1.0, summary.DesignEffect);
        Assert.Empty(summary.Bins);
    }

    [Fact]
    public void Summarize_CountsTheAttemptsOfAReplacedFailedPredecessor()
    {
        var rows = Block(10, 0, 4, 10).Concat(Block(11, 1, 0, 10)).ToList();
        var runs = new[]
        {
            Run(10, selected: false),
            Run(11, replaces: 10, cause: BenchmarkReplacementCause.ProviderFailure),
        };

        var summary = BenchmarkProviderAvailability.Summarize(rows, runs);

        Assert.Equal(20, summary.ValidAttempts);
        Assert.Equal(4, summary.ProviderErrors);
        Assert.Equal(2, summary.RecordedRuns);
        Assert.Equal(1, summary.ReplacedRunsIncluded);
        Assert.Equal(1, summary.ReplacementRuns);
        Assert.Equal(1, summary.ProviderReplacementRuns);
        Assert.Equal(1, summary.RerunsNeeded);
        Assert.Equal(1, summary.ProviderRerunsNeeded);
    }

    [Fact]
    public void RerunsNeeded_WalksTheLineageChain()
    {
        // Run 3 replaced run 2 (our failure), which replaced run 1 (provider failure); run 4's
        // predecessor is outside the pool and does not count.
        var runs = new[]
        {
            Run(1, selected: false, repairs: 1, providerRepairs: 1),
            Run(2, selected: false, replaces: 1, cause: BenchmarkReplacementCause.ProviderFailure),
            Run(3, replaces: 2, cause: BenchmarkReplacementCause.OurFailure, repairs: 2, providerRepairs: 1),
            Run(4, replaces: 99, cause: BenchmarkReplacementCause.ProviderFailure),
        };

        var counts = BenchmarkProviderAvailability.CountReruns(runs);

        Assert.Equal(3, counts.RepairPasses);
        Assert.Equal(2, counts.ProviderRepairPasses);
        Assert.Equal(2, counts.ReplacementRuns);
        Assert.Equal(1, counts.ProviderReplacementRuns);
        Assert.Equal(5, counts.RerunsNeeded);
        Assert.Equal(3, counts.ProviderRerunsNeeded);

        var summary = BenchmarkProviderAvailability.Summarize(Array.Empty<ProviderAttemptRow>(), runs);

        Assert.Equal(2, summary.ReplacedRunsIncluded);
        Assert.Equal(3, summary.RepairPasses);
        Assert.Equal(2, summary.ProviderRepairPasses);
        Assert.Equal(2, summary.ReplacementRuns);
        Assert.Equal(1, summary.ProviderReplacementRuns);
        Assert.Equal(5, summary.RerunsNeeded);
        Assert.Equal(3, summary.ProviderRerunsNeeded);
    }

    [Fact]
    public void Summarize_BinsAreContiguousFiveMinuteUtcBins()
    {
        var rows = new[]
        {
            Attempt(1, ProviderAttemptOutcome.Ok, T0.AddMinutes(1), callIndex: 0),
            Attempt(1, ProviderAttemptOutcome.ProviderOverloaded, T0.AddMinutes(3), callIndex: 1),
            Attempt(1, ProviderAttemptOutcome.OurThrottle, T0.AddMinutes(22), callIndex: 2),
            Attempt(1, ProviderAttemptOutcome.RateLimitUnattributed, T0.AddMinutes(24), callIndex: 3),
            Attempt(1, ProviderAttemptOutcome.Ok, T0.AddHours(2), source: ModelCallSource.BenchmarkGrader),
        };

        var bins = BenchmarkProviderAvailability.Summarize(rows, new[] { Run(1) }).Bins;

        Assert.Equal(5, bins.Count);
        for (int i = 0; i < bins.Count; i++)
        {
            Assert.Equal(T0.AddMinutes(5 * i), bins[i].StartUtc);
            Assert.Equal(DateTimeKind.Utc, bins[i].StartUtc.Kind);
        }

        Assert.Equal(new ProviderAvailabilityBin(T0, 2, 1, 0, 0), bins[0]);
        Assert.All(bins.Skip(1).Take(3), b => Assert.Equal(0, b.Valid + b.Ours));
        Assert.Equal(new ProviderAvailabilityBin(T0.AddMinutes(20), 1, 0, 1, 1), bins[4]);
    }

    [Fact]
    public void Summarize_BurstInOneTimeBlockWidensTheInterval()
    {
        var spread = Enumerable.Range(0, 10).SelectMany(b => Block(1, b, 1, 50, callBase: b * 50)).ToList();
        var burst = Enumerable.Range(0, 10).SelectMany(b => Block(1, b, b == 0 ? 10 : 0, 50, callBase: b * 50)).ToList();

        var even = BenchmarkProviderAvailability.Summarize(spread, new[] { Run(1) });
        var bursty = BenchmarkProviderAvailability.Summarize(burst, new[] { Run(1) });

        Assert.Equal(even.ProviderErrorRate, bursty.ProviderErrorRate);
        Assert.Equal(1.0, even.DesignEffect);
        Assert.True(bursty.DesignEffect > 1.0);
        Assert.True(bursty.ProviderErrorRateLow < even.ProviderErrorRateLow);
        Assert.True(bursty.ProviderErrorRateHigh > even.ProviderErrorRateHigh);
        Assert.False(even.FewAttempts);
    }

    [Fact]
    public void Summarize_UsesRunClustersWhenTheyGiveTheLargerDesignEffect()
    {
        var rows = Enumerable.Range(0, 5).SelectMany(b => Block(1, b, 2, 100, callBase: b * 100))
            .Concat(Enumerable.Range(0, 5).SelectMany(b => Block(2, b, 0, 100, callBase: b * 100)))
            .ToList();

        var summary = BenchmarkProviderAvailability.Summarize(rows, new[] { Run(1), Run(2) });

        var byRun = BenchmarkProviderAvailability.ClusterAdjustedWilson95(new[] { ("1", 10, 500), ("2", 0, 500) });
        var byBlock = BenchmarkProviderAvailability.ClusterAdjustedWilson95(
            Enumerable.Range(0, 5).Select(b => ($"1@{b}", 2, 100)).Concat(Enumerable.Range(0, 5).Select(b => ($"2@{b}", 0, 100))));
        Assert.True(byRun.Deff > byBlock.Deff);
        Assert.Equal(byRun.Deff, summary.DesignEffect);
        Assert.Equal(byRun.Low, summary.ProviderErrorRateLow);
        Assert.Equal(byRun.High, summary.ProviderErrorRateHigh);

        var single = BenchmarkProviderAvailability.Summarize(rows.Where(r => r.RunId == 1).ToList(), new[] { Run(1) });
        Assert.Equal(1.0, single.DesignEffect);
    }
}
