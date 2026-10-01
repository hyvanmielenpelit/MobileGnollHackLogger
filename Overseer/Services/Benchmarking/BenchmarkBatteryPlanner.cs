namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using MobileGnollHackLogger.Data;

/// <summary>
/// The slot plan of a battery run and the usability rule for its members (Statistical Method M4).
/// Pure computation: no I/O, no writes.
///
/// <para>A battery run has <c>K × R</c> slots, one per (suite index, round). They are filled in
/// round-robin order — every suite once, then every suite again — so the battery is complete after
/// the first round and provider drift is spread across suites. A slot is <i>occupied</i> by any
/// non-superseded member, usable or not; the caller decides which members occupy.</para>
/// </summary>
public static class BenchmarkBatteryPlanner
{
    public const string SupersededReason = "superseded";
    public const string GuardFailureReasonPrefix = "instrument guard: ";
    public const string RunFailedReason = "run failed";
    public const string RunCanceledReason = "run canceled";
    public const string RunNotFinishedReason = "run not finished";
    public const string IndexWithheldProviderFailureReason = "index withheld (provider failure)";
    public const string IndexWithheldReason = "index withheld";

    /// <summary>
    /// The first unoccupied slot in round-major, suite-minor order — round 1 for every suite, then
    /// round 2 — or null when every slot is occupied. Rounds are 1-based, suite indices 0-based.
    /// </summary>
    public static (int SuiteIndex, int Round)? NextMember(
        int suiteCount,
        int runsPerSuite,
        IReadOnlyCollection<(int SuiteIndex, int Round)> occupied)
    {
        var taken = Occupied(suiteCount, runsPerSuite, occupied);

        for (int round = 1; round <= runsPerSuite; round++)
        {
            for (int suiteIndex = 0; suiteIndex < suiteCount; suiteIndex++)
            {
                if (!taken.Contains((suiteIndex, round))) return (suiteIndex, round);
            }
        }

        return null;
    }

    /// <summary>The number of unoccupied slots; occupied pairs outside the grid are ignored.</summary>
    public static int RemainingLaunches(
        int suiteCount,
        int runsPerSuite,
        IReadOnlyCollection<(int SuiteIndex, int Round)> occupied)
    {
        var taken = Occupied(suiteCount, runsPerSuite, occupied);
        int inGrid = taken.Count(s => s.SuiteIndex >= 0 && s.SuiteIndex < suiteCount && s.Round >= 1 && s.Round <= runsPerSuite);
        return suiteCount * runsPerSuite - inGrid;
    }

    /// <summary>
    /// True when the member may enter a statistic: it is not superseded, carries no guard failure,
    /// its run ended Completed, CompletedWithLimits or CompletedWithErrors, and the run's
    /// <see cref="BenchmarkRun.QualityIndex"/> is not null. The last condition is not implied by the
    /// status: the finalizer withholds the index of a run that lost a question at the provider and
    /// still reports it CompletedWithErrors.
    /// </summary>
    public static bool IsUsable(BenchmarkBatteryRunMember member, BenchmarkRun run)
        => UnusableReason(member, run) == null;

    /// <summary>Why the member is not usable, in a few words; null when it is.</summary>
    public static string? UnusableReason(BenchmarkBatteryRunMember member, BenchmarkRun run)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(run);

        if (member.Superseded) return SupersededReason;
        if (!string.IsNullOrWhiteSpace(member.GuardFailure)) return GuardFailureReasonPrefix + member.GuardFailure.Trim();

        switch (run.Status)
        {
            case BenchmarkRunStatus.Failed:
                return RunFailedReason;
            case BenchmarkRunStatus.Canceled:
                return RunCanceledReason;
            case BenchmarkRunStatus.Running:
                return RunNotFinishedReason;
            case BenchmarkRunStatus.Completed:
            case BenchmarkRunStatus.CompletedWithLimits:
            case BenchmarkRunStatus.CompletedWithErrors:
                break;
            default:
                return $"run status {run.Status}";
        }

        if (!run.QualityIndex.HasValue)
        {
            return run.TerminalFailureAnswerCount > 0 ? IndexWithheldProviderFailureReason : IndexWithheldReason;
        }

        return null;
    }

    private static HashSet<(int SuiteIndex, int Round)> Occupied(
        int suiteCount,
        int runsPerSuite,
        IReadOnlyCollection<(int SuiteIndex, int Round)>? occupied)
    {
        if (suiteCount < 1) throw new ArgumentOutOfRangeException(nameof(suiteCount), suiteCount, "A battery run has at least one suite.");
        if (runsPerSuite < 1) throw new ArgumentOutOfRangeException(nameof(runsPerSuite), runsPerSuite, "A battery run has at least one round.");

        return new HashSet<(int SuiteIndex, int Round)>(occupied ?? Array.Empty<(int, int)>());
    }
}
