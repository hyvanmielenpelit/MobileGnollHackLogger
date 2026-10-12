namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MobileGnollHackLogger.Data;

/// <summary>One ledger row as the availability calculator reads it.</summary>
public sealed record ProviderAttemptRow(
    long RunId,
    long? AnswerId,
    ModelCallSource Source,
    int Pass,
    int CallIndex,
    int AttemptIndex,
    DateTime StartedAtUtc,
    int DurationMs,
    ProviderAttemptOutcome Outcome,
    int? HttpStatus,
    string? ProviderErrorCode,
    bool Retried);

/// <summary>
/// One run of a pooled figure: a selected run, or a lineage predecessor loaded through
/// <see cref="BenchmarkRun.ReplacesRunId"/>. <paramref name="Recorded"/> is
/// <see cref="BenchmarkRun.ProviderLedgerVersion"/> being set; <paramref name="QuestionsLostToProvider"/>
/// counts the run's answers whose terminal status is <c>ProviderError</c> with origin <c>Provider</c>.
/// </summary>
public sealed record RunLineageRow(
    long RunId,
    bool Selected,
    bool Recorded,
    long? ReplacesRunId,
    BenchmarkReplacementCause? ReplacementCause,
    int RepairPassCount,
    int ProviderRepairPassCount,
    int QuestionsLostToProvider);

/// <summary>One 5-minute UTC bin of the attempt strip.</summary>
public sealed record ProviderAvailabilityBin(DateTime StartUtc, int Valid, int Provider, int Unattributed, int Ours);

/// <summary>
/// Re-run counts of a pool of runs. A replacement run is a run of the pool that another run of the
/// pool replaced; it is provider-caused when its replacing run carries
/// <see cref="BenchmarkReplacementCause.ProviderFailure"/>.
/// </summary>
public sealed record ProviderRerunCounts(int RepairPasses, int ProviderRepairPasses, int ReplacementRuns, int ProviderReplacementRuns)
{
    /// <summary>Repair passes plus replacement runs.</summary>
    public int RerunsNeeded => RepairPasses + ReplacementRuns;

    /// <summary>The provider-caused part of <see cref="RerunsNeeded"/>.</summary>
    public int ProviderRerunsNeeded => ProviderRepairPasses + ProviderReplacementRuns;
}

/// <summary>
/// The provider availability of one run or a pool of runs (docs/overseer/ai-benchmark-provider-availability.md).
/// Every rate and count is candidate-only; the grader figures are informational. Rates are proportions
/// in [0, 1]; null when there is no valid attempt.
/// </summary>
public sealed record ProviderAvailabilitySummary
{
    /// <summary>At least one run of the pool records the ledger.</summary>
    public bool Recorded { get; init; }
    public int RecordedRuns { get; init; }
    public int TotalRuns { get; init; }

    /// <summary>Runs in the pool that a later run replaced (lineage predecessors).</summary>
    public int ReplacedRunsIncluded { get; init; }

    public int ValidAttempts { get; init; }
    public int ProviderErrors { get; init; }
    public int UnattributedErrors { get; init; }
    public int OurSideFailures { get; init; }

    public double? ProviderErrorRate { get; init; }
    public double? ProviderErrorRateLow { get; init; }
    public double? ProviderErrorRateHigh { get; init; }

    /// <summary>The design effect behind <see cref="ProviderErrorRateLow"/> and <see cref="ProviderErrorRateHigh"/>; 1 means no clustering adjustment.</summary>
    public double DesignEffect { get; init; } = 1.0;

    /// <summary>(provider + unattributed) ÷ valid: the upper bound including unattributed errors.</summary>
    public double? InclusiveRate { get; init; }
    public double? InclusiveRateLow { get; init; }
    public double? InclusiveRateHigh { get; init; }

    /// <summary>A recorded pool with fewer than <see cref="BenchmarkProviderAvailability.FewAttemptsThreshold"/> valid attempts.</summary>
    public bool FewAttempts { get; init; }

    // --- By class ---------------------------------------------------------------------------------
    public int Overloaded { get; init; }
    public int ServerErrors { get; init; }
    public int Timeouts { get; init; }
    public int StreamFailures { get; init; }
    public int RateLimitedOurs { get; init; }
    public int RateLimitedUnattributed { get; init; }
    public int TransportUnattributed { get; init; }

    /// <summary>Candidate attempt counts per outcome class, every class present (zero when unseen).</summary>
    public IReadOnlyDictionary<ProviderAttemptOutcome, int> CountsByOutcome { get; init; } = new Dictionary<ProviderAttemptOutcome, int>();

    // --- Answers, calls and re-runs ---------------------------------------------------------------
    /// <summary>Answers of the recorded runs whose terminal status is <c>ProviderError</c> with origin <c>Provider</c>.</summary>
    public int QuestionsLost { get; init; }

    /// <summary>Candidate calls with at least one valid attempt, and those whose first attempt was <c>Ok</c>.</summary>
    public int Calls { get; init; }
    public int FirstAttemptSuccessCalls { get; init; }

    /// <summary>The re-run figures cover every run of the pool, recorded or not.</summary>
    public int RepairPasses { get; init; }
    public int ProviderRepairPasses { get; init; }
    public int ReplacementRuns { get; init; }
    public int ProviderReplacementRuns { get; init; }
    public int RerunsNeeded { get; init; }
    public int ProviderRerunsNeeded { get; init; }

    // --- Grader (information only) ----------------------------------------------------------------
    public int GraderValidAttempts { get; init; }
    public int GraderProviderErrors { get; init; }

    /// <summary>
    /// 5-minute UTC bins from the first to the last candidate attempt, contiguous, empty bins included.
    /// Empty when the span needs more than <see cref="BenchmarkProviderAvailability.MaxBins"/> bins.
    /// </summary>
    public IReadOnlyList<ProviderAvailabilityBin> Bins { get; init; } = Array.Empty<ProviderAvailabilityBin>();

    /// <summary>A summary for a pool in which no run records the ledger.</summary>
    public static ProviderAvailabilitySummary NotRecorded(int totalRuns) => new() { TotalRuns = totalRuns };
}

/// <summary>
/// Pure calculator for GnollBench provider availability: the summary, the cluster-adjusted Wilson
/// interval, re-runs needed, 5-minute bins and the formatting the client mirrors exactly.
/// </summary>
public static class BenchmarkProviderAvailability
{
    /// <summary>Below this many valid attempts a figure carries the tag "few attempts".</summary>
    public const int FewAttemptsThreshold = 100;

    /// <summary>Width of one strip bin.</summary>
    public static readonly TimeSpan BinWidth = TimeSpan.FromMinutes(5);

    /// <summary>Width of the time block that clusters attempts for the design effect.</summary>
    public static readonly TimeSpan ClusterBlockWidth = TimeSpan.FromMinutes(10);

    /// <summary>The most bins a summary carries (30 days of 5-minute bins); a longer span gets none.</summary>
    public const int MaxBins = 8_640;

    /// <summary>
    /// Summarizes a pool of runs. Only rows of runs that are in <paramref name="runs"/> and recorded
    /// count; the re-run figures cover every run of the pool. With <paramref name="candidateOnly"/>
    /// the attempt figures count candidate rows only (the reported figures); otherwise every row. The
    /// grader figures always count grader rows. The interval is the cluster-adjusted Wilson 95 %
    /// interval: clusters are (run, 10-minute UTC block) and, when two or more recorded runs are
    /// pooled, also runs, and the larger design effect is used.
    /// </summary>
    public static ProviderAvailabilitySummary Summarize(IReadOnlyList<ProviderAttemptRow> rows, IReadOnlyList<RunLineageRow> runs, bool candidateOnly = true)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(runs);

        var pool = new Dictionary<long, RunLineageRow>();
        foreach (var run in runs)
        {
            pool.TryAdd(run.RunId, run);
        }

        var recorded = pool.Values.Where(r => r.Recorded).Select(r => r.RunId).ToHashSet();
        int replacedIncluded = pool.Values.Count(r => !r.Selected);
        var reruns = CountReruns(pool.Values);

        if (recorded.Count == 0)
        {
            return ProviderAvailabilitySummary.NotRecorded(pool.Count) with
            {
                ReplacedRunsIncluded = replacedIncluded,
                RepairPasses = reruns.RepairPasses,
                ProviderRepairPasses = reruns.ProviderRepairPasses,
                ReplacementRuns = reruns.ReplacementRuns,
                ProviderReplacementRuns = reruns.ProviderReplacementRuns,
                RerunsNeeded = reruns.RerunsNeeded,
                ProviderRerunsNeeded = reruns.ProviderRerunsNeeded,
            };
        }

        var counts = Enum.GetValues<ProviderAttemptOutcome>().ToDictionary(o => o, _ => 0);
        int graderValid = 0, graderProvider = 0;
        var primary = new List<ProviderAttemptRow>();
        foreach (var row in rows)
        {
            if (!recorded.Contains(row.RunId)) continue;

            if (row.Source == ModelCallSource.BenchmarkGrader && ProviderAttemptOutcomes.IsValid(row.Outcome))
            {
                graderValid++;
                if (ProviderAttemptOutcomes.IsProvider(row.Outcome)) graderProvider++;
            }

            if (candidateOnly && row.Source != ModelCallSource.BenchmarkCandidate) continue;

            primary.Add(row);
            counts[row.Outcome] = counts.GetValueOrDefault(row.Outcome) + 1;
        }

        int valid = 0, provider = 0, unattributed = 0, ours = 0;
        foreach (var (outcome, count) in counts)
        {
            if (ProviderAttemptOutcomes.IsValid(outcome)) valid += count;
            if (ProviderAttemptOutcomes.IsProvider(outcome)) provider += count;
            if (ProviderAttemptOutcomes.IsUnattributed(outcome)) unattributed += count;
            if (ProviderAttemptOutcomes.IsOurs(outcome)) ours += count;
        }

        var (strict, inclusive) = PoolIntervals(primary, recorded.Count);

        var (calls, firstAttemptSuccess) = CountCalls(primary);

        return new ProviderAvailabilitySummary
        {
            Recorded = true,
            RecordedRuns = recorded.Count,
            TotalRuns = pool.Count,
            ReplacedRunsIncluded = replacedIncluded,

            ValidAttempts = valid,
            ProviderErrors = provider,
            UnattributedErrors = unattributed,
            OurSideFailures = ours,

            ProviderErrorRate = valid == 0 ? null : (double)provider / valid,
            ProviderErrorRateLow = strict.Low,
            ProviderErrorRateHigh = strict.High,
            DesignEffect = strict.Deff,

            InclusiveRate = valid == 0 ? null : (double)(provider + unattributed) / valid,
            InclusiveRateLow = inclusive.Low,
            InclusiveRateHigh = inclusive.High,

            FewAttempts = valid < FewAttemptsThreshold,

            Overloaded = counts[ProviderAttemptOutcome.ProviderOverloaded],
            ServerErrors = counts[ProviderAttemptOutcome.ProviderServerError],
            Timeouts = counts[ProviderAttemptOutcome.ProviderTimeout],
            StreamFailures = counts[ProviderAttemptOutcome.ProviderStreamFailure],
            RateLimitedOurs = counts[ProviderAttemptOutcome.OurRateLimit],
            RateLimitedUnattributed = counts[ProviderAttemptOutcome.RateLimitUnattributed],
            TransportUnattributed = counts[ProviderAttemptOutcome.TransportUnattributed],
            CountsByOutcome = counts,

            QuestionsLost = pool.Values.Where(r => r.Recorded).Sum(r => r.QuestionsLostToProvider),
            Calls = calls,
            FirstAttemptSuccessCalls = firstAttemptSuccess,

            RepairPasses = reruns.RepairPasses,
            ProviderRepairPasses = reruns.ProviderRepairPasses,
            ReplacementRuns = reruns.ReplacementRuns,
            ProviderReplacementRuns = reruns.ProviderReplacementRuns,
            RerunsNeeded = reruns.RerunsNeeded,
            ProviderRerunsNeeded = reruns.ProviderRerunsNeeded,

            GraderValidAttempts = graderValid,
            GraderProviderErrors = graderProvider,

            Bins = BuildBins(primary),
        };
    }

    /// <summary>
    /// Re-run counts of a pool: the sum of every run's repair passes (and their provider part), plus the
    /// runs of the pool that another run of the pool replaced through <see cref="RunLineageRow.ReplacesRunId"/>.
    /// A replaced run counts once, and is provider-caused when any run replacing it carries
    /// <see cref="BenchmarkReplacementCause.ProviderFailure"/>. Duplicate run ids count once.
    /// </summary>
    public static ProviderRerunCounts CountReruns(IEnumerable<RunLineageRow> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);

        var pool = new Dictionary<long, RunLineageRow>();
        foreach (var run in runs)
        {
            pool.TryAdd(run.RunId, run);
        }

        int repairPasses = 0, providerRepairPasses = 0;
        var replaced = new Dictionary<long, bool>();
        foreach (var run in pool.Values)
        {
            repairPasses += run.RepairPassCount;
            providerRepairPasses += run.ProviderRepairPassCount;

            if (run.ReplacesRunId is long predecessor && predecessor != run.RunId && pool.ContainsKey(predecessor))
            {
                bool byProvider = run.ReplacementCause == BenchmarkReplacementCause.ProviderFailure;
                replaced[predecessor] = replaced.GetValueOrDefault(predecessor) || byProvider;
            }
        }

        return new ProviderRerunCounts(repairPasses, providerRepairPasses, replaced.Count, replaced.Values.Count(v => v));
    }

    /// <summary>
    /// The 95 % Wilson interval on the design-effect-adjusted sample of the given clusters, where
    /// <c>K</c> counts provider-class (the "successes" of the proportion) and <c>N</c> valid attempts.
    /// Entries with the same cluster key are summed, and clusters with no valid attempt are left out.
    /// With p̂ = ΣK / ΣN, N = ΣN and C clusters, V_cl = C/(C−1) · Σ(K_c − p̂·N_c)² / N² and
    /// V_srs = p̂(1−p̂)/N; the design effect is max(1, V_cl / V_srs) when 0 &lt; p̂ &lt; 1 and C ≥ 2,
    /// otherwise 1. The interval is <see cref="BenchmarkProportionInterval.Wilson95(double, double)"/>
    /// on n_eff = N / DEFF and k_eff = p̂ · n_eff; with a design effect of 1 it is the plain Wilson
    /// interval on (ΣK, ΣN). Low and High are null when there is no valid attempt.
    /// </summary>
    public static (double? Low, double? High, double Deff) ClusterAdjustedWilson95(IEnumerable<(string Cluster, int K, int N)> clusters)
    {
        ArgumentNullException.ThrowIfNull(clusters);

        var totals = new Dictionary<string, (long K, long N)>(StringComparer.Ordinal);
        foreach (var (cluster, k, n) in clusters)
        {
            if (n < 0) throw new ArgumentOutOfRangeException(nameof(clusters), n, "A cluster's valid attempt count cannot be negative.");
            if (k < 0 || k > n) throw new ArgumentOutOfRangeException(nameof(clusters), k, "A cluster's error count must lie between 0 and its valid attempt count.");

            string key = cluster ?? string.Empty;
            var (sumK, sumN) = totals.GetValueOrDefault(key);
            totals[key] = (sumK + k, sumN + n);
        }

        var used = totals.Values.Where(t => t.N > 0).ToList();
        long totalK = used.Sum(t => t.K);
        long totalN = used.Sum(t => t.N);
        if (totalN == 0) return (null, null, 1.0);

        double total = totalN;
        double p = totalK / total;
        double deff = 1.0;
        int c = used.Count;
        if (c >= 2 && p > 0.0 && p < 1.0)
        {
            double sumSquares = 0.0;
            foreach (var (clusterK, clusterN) in used)
            {
                double residual = clusterK - p * clusterN;
                sumSquares += residual * residual;
            }

            double clusterVariance = c / (c - 1.0) * sumSquares / (total * total);
            double simpleVariance = p * (1.0 - p) / total;
            deff = Math.Max(1.0, clusterVariance / simpleVariance);
        }

        (double Low, double High)? interval;
        if (deff == 1.0)
        {
            interval = BenchmarkProportionInterval.Wilson95((double)totalK, total);
        }
        else
        {
            double nEff = total / deff;
            interval = BenchmarkProportionInterval.Wilson95(p * nEff, nEff);
        }

        return interval is { } bounds ? ((double?)bounds.Low, (double?)bounds.High, deff) : (null, null, deff);
    }

    // --- Formatting ---------------------------------------------------------------------------------
    // Invariant culture throughout; the Angular client (provider-availability.ts) mirrors every rule
    // byte for byte, and BenchmarkProviderAvailabilityTests holds the shared parity table.

    /// <summary>
    /// A proportion as a percentage with 2 significant digits and a regular space before the sign:
    /// <c>0.81 %</c>, <c>3.1 %</c>, <c>3.0 %</c>, <c>12 %</c>, <c>0.0042 %</c>; <c>0 %</c> for zero and
    /// <c>100 %</c> for one. See <see cref="FormatPercentNumber"/> for the rounding rule.
    /// </summary>
    public static string FormatPercent(double proportion) => FormatPercentNumber(proportion) + " %";

    /// <summary>
    /// The number part of <see cref="FormatPercent"/>, without the percent sign. The proportion is
    /// clamped to [0, 1] and v = proportion × 100. Then:
    /// <list type="number">
    /// <item>v = 0 gives <c>0</c>.</item>
    /// <item>The decimals d are 0 when v ≥ 10; otherwise the smallest d ≥ 1 for which v × 10^d ≥ 10
    /// (10^d by repeated multiplication of 10.0). Beyond 15 decimals the value gives <c>0</c>.</item>
    /// <item>r = floor(v × 10^d + 0.5): round half away from zero.</item>
    /// <item>When d &gt; 0 and r reaches 100 (9.96 → 10), d drops by one and r is rounded again.</item>
    /// <item>r is written in digits with the decimal point d places from the right, padded with
    /// leading zeros so that one digit precedes the point; trailing zeros are kept.</item>
    /// </list>
    /// So values of 10 % and more are whole numbers (<c>12</c>, <c>100</c>), and smaller values keep
    /// two significant digits (<c>3.0</c>, <c>0.81</c>, <c>0.0042</c>).
    /// </summary>
    public static string FormatPercentNumber(double proportion)
    {
        if (double.IsNaN(proportion)) throw new ArgumentOutOfRangeException(nameof(proportion), proportion, "The proportion must be a number.");

        double v = Math.Min(1.0, Math.Max(0.0, proportion)) * 100.0;
        if (v == 0.0) return "0";

        const int MaxDecimals = 15;
        int d = 0;
        double scale = 1.0;
        if (v < 10.0)
        {
            do
            {
                d++;
                scale *= 10.0;
            }
            while (v * scale < 10.0 && d < MaxDecimals);

            if (v * scale < 10.0) return "0";
        }

        double r = Math.Floor(v * scale + 0.5);
        if (d > 0 && r >= 100.0)
        {
            d--;
            scale /= 10.0;
            r = Math.Floor(v * scale + 0.5);
        }

        string digits = ((long)r).ToString(CultureInfo.InvariantCulture);
        if (d == 0) return digits;

        digits = digits.PadLeft(d + 1, '0');
        return digits[..^d] + "." + digits[^d..];
    }

    /// <summary>
    /// The count with its denominator: <c>12 of 1,480 attempts</c>; <c>1 of 1 attempt</c> when
    /// <paramref name="n"/> is 1. Numbers use the "," thousands separator and no decimals.
    /// </summary>
    public static string FormatCount(int k, int n) =>
        FormatInteger(k) + " of " + FormatInteger(n) + (n == 1 ? " attempt" : " attempts");

    /// <summary>
    /// The interval <c>95 % CI 0.46–1.4 %</c>: each bound by <see cref="FormatPercentNumber"/>, joined
    /// by an en dash (U+2013), one percent sign at the end. Empty when either bound is null.
    /// </summary>
    public static string FormatInterval(double? low, double? high) =>
        low is double l && high is double h
            ? "95 % CI " + FormatPercentNumber(l) + "–" + FormatPercentNumber(h) + " %"
            : string.Empty;

    /// <summary>
    /// The natural frequency for prose, <c>about 1 in 123</c>, when 0 &lt; k/n &lt; 1 % (tested in
    /// integers as k × 100 &lt; n); N = floor(n / k + 0.5) with the "," thousands separator. Null otherwise.
    /// </summary>
    public static string? FormatOneIn(int k, int n)
    {
        if (k <= 0 || n <= 0 || (long)k * 100 >= n) return null;

        long oneIn = (long)Math.Floor((double)n / k + 0.5);
        return "about 1 in " + FormatInteger(oneIn);
    }

    /// <summary>
    /// The headline: <see cref="FormatPercent"/> of k/n, then in parentheses <see cref="FormatCount"/>,
    /// <see cref="FormatInterval"/> when it is not empty, and <c>few attempts</c> when
    /// n &lt; <see cref="FewAttemptsThreshold"/>, separated by "; ". For example
    /// <c>0.81 % (12 of 1,480 attempts; 95 % CI 0.46–1.4 %)</c> and
    /// <c>3.0 % (3 of 99 attempts; 95 % CI 1.0–8.5 %; few attempts)</c>. With n = 0 it is
    /// <c>no valid attempts</c>. A pool that is not recorded is the caller's to word.
    /// </summary>
    public static string FormatRate(int k, int n, double? low, double? high)
    {
        if (n <= 0) return "no valid attempts";

        var parts = new List<string> { FormatCount(k, n) };
        string interval = FormatInterval(low, high);
        if (interval.Length > 0) parts.Add(interval);
        if (n < FewAttemptsThreshold) parts.Add("few attempts");

        return FormatPercent((double)k / n) + " (" + string.Join("; ", parts) + ")";
    }

    private static string FormatInteger(long value) => value.ToString("#,0", CultureInfo.InvariantCulture);

    // --- Helpers ------------------------------------------------------------------------------------

    private static ((double? Low, double? High, double Deff) Strict, (double? Low, double? High, double Deff) Inclusive) PoolIntervals(
        IReadOnlyList<ProviderAttemptRow> primary, int recordedRuns)
    {
        long blockTicks = ClusterBlockWidth.Ticks;
        var byBlock = new Dictionary<(long Run, long Block), (int Provider, int Inclusive, int Valid)>();
        var byRun = new Dictionary<long, (int Provider, int Inclusive, int Valid)>();
        foreach (var row in primary)
        {
            if (!ProviderAttemptOutcomes.IsValid(row.Outcome)) continue;

            int p = ProviderAttemptOutcomes.IsProvider(row.Outcome) ? 1 : 0;
            int i = p == 1 || ProviderAttemptOutcomes.IsUnattributed(row.Outcome) ? 1 : 0;

            var blockKey = (row.RunId, row.StartedAtUtc.Ticks / blockTicks);
            var b = byBlock.GetValueOrDefault(blockKey);
            byBlock[blockKey] = (b.Provider + p, b.Inclusive + i, b.Valid + 1);

            var r = byRun.GetValueOrDefault(row.RunId);
            byRun[row.RunId] = (r.Provider + p, r.Inclusive + i, r.Valid + 1);
        }

        string BlockName((long Run, long Block) key) => key.Run.ToString(CultureInfo.InvariantCulture) + "@" + key.Block.ToString(CultureInfo.InvariantCulture);
        string RunName(long run) => run.ToString(CultureInfo.InvariantCulture);

        var strict = ClusterAdjustedWilson95(byBlock.Select(e => (BlockName(e.Key), e.Value.Provider, e.Value.Valid)));
        var inclusive = ClusterAdjustedWilson95(byBlock.Select(e => (BlockName(e.Key), e.Value.Inclusive, e.Value.Valid)));

        if (recordedRuns >= 2)
        {
            var strictByRun = ClusterAdjustedWilson95(byRun.Select(e => (RunName(e.Key), e.Value.Provider, e.Value.Valid)));
            var inclusiveByRun = ClusterAdjustedWilson95(byRun.Select(e => (RunName(e.Key), e.Value.Inclusive, e.Value.Valid)));
            if (strictByRun.Deff > strict.Deff) strict = strictByRun;
            if (inclusiveByRun.Deff > inclusive.Deff) inclusive = inclusiveByRun;
        }

        return (strict, inclusive);
    }

    private static (int Calls, int FirstAttemptSuccess) CountCalls(IReadOnlyList<ProviderAttemptRow> primary)
    {
        int calls = 0, firstAttemptSuccess = 0;
        foreach (var call in primary.GroupBy(r => (r.RunId, r.AnswerId, r.Source, r.Pass, r.CallIndex)))
        {
            if (!call.Any(r => ProviderAttemptOutcomes.IsValid(r.Outcome))) continue;

            calls++;
            if (call.Any(r => r.AttemptIndex == 0 && r.Outcome == ProviderAttemptOutcome.Ok)) firstAttemptSuccess++;
        }

        return (calls, firstAttemptSuccess);
    }

    private static IReadOnlyList<ProviderAvailabilityBin> BuildBins(IReadOnlyList<ProviderAttemptRow> primary)
    {
        if (primary.Count == 0) return Array.Empty<ProviderAvailabilityBin>();

        long width = BinWidth.Ticks;
        long first = primary.Min(r => r.StartedAtUtc.Ticks) / width;
        long last = primary.Max(r => r.StartedAtUtc.Ticks) / width;
        long count = last - first + 1;
        if (count > MaxBins) return Array.Empty<ProviderAvailabilityBin>();

        var valid = new int[count];
        var provider = new int[count];
        var unattributed = new int[count];
        var ours = new int[count];
        foreach (var row in primary)
        {
            long index = row.StartedAtUtc.Ticks / width - first;
            if (ProviderAttemptOutcomes.IsValid(row.Outcome)) valid[index]++;
            if (ProviderAttemptOutcomes.IsProvider(row.Outcome)) provider[index]++;
            if (ProviderAttemptOutcomes.IsUnattributed(row.Outcome)) unattributed[index]++;
            if (ProviderAttemptOutcomes.IsOurs(row.Outcome)) ours[index]++;
        }

        var bins = new ProviderAvailabilityBin[count];
        for (long i = 0; i < count; i++)
        {
            bins[i] = new ProviderAvailabilityBin(new DateTime((first + i) * width, DateTimeKind.Utc), valid[i], provider[i], unattributed[i], ours[i]);
        }

        return bins;
    }
}
