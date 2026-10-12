namespace Overseer.Services.ChatConsistency;

using System;
using System.Collections.Generic;
using System.Linq;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;

/// <summary>
/// A run a chat consistency analysis reads for provider availability (P6) only: a failed or canceled run
/// inside a period's run range, a failed or superseded member of a compared battery run, or a lineage
/// predecessor of an analyzed run. <paramref name="UnitId"/> is the unit it joins: its battery run in a
/// battery set, the run itself otherwise.
/// </summary>
public sealed record ChatConsistencyAvailabilityRun(BenchmarkRun Run, string Period, long UnitId, long? BatteryRunId);

/// <summary>
/// The provider availability figures chat consistency reads from the attempt ledger and the run rollups
/// (docs/overseer/ai-benchmark-provider-availability.md), candidate attempts only. Pure.
/// </summary>
public static class ChatConsistencyAvailability
{
    /// <summary>The run statuses whose recorded attempts enter provider availability only.</summary>
    public static readonly IReadOnlyList<BenchmarkRunStatus> AvailabilityOnlyStatuses = new[]
    {
        BenchmarkRunStatus.Failed, BenchmarkRunStatus.Canceled
    };

    /// <summary>The run recorded the provider attempt ledger (harness 56 and later).</summary>
    public static bool IsRecorded(BenchmarkRun run) => run.ProviderLedgerVersion.HasValue;

    /// <summary>A failed or canceled run.</summary>
    public static bool IsAvailabilityOnlyStatus(BenchmarkRunStatus status) => status is BenchmarkRunStatus.Failed or BenchmarkRunStatus.Canceled;

    /// <summary>The run as the availability calculator's pool reads it; questions lost only from loaded answers.</summary>
    public static RunLineageRow LineageRow(BenchmarkRun run, bool selected)
        => new(
            run.Id,
            selected,
            IsRecorded(run),
            run.ReplacesRunId,
            run.ReplacementCause,
            run.RepairPassCount,
            run.ProviderRepairPassCount,
            (run.Answers ?? new List<BenchmarkRunAnswer>()).Count(a =>
                a.Status == BenchmarkAnswerStatus.ProviderError && a.FailureOrigin == BenchmarkFailureOrigin.Provider));

    /// <summary>
    /// Every predecessor of <paramref name="runs"/> through <see cref="BenchmarkRun.ReplacesRunId"/> that
    /// <paramref name="known"/> holds, walking each chain to its start; the runs themselves left out.
    /// </summary>
    public static List<BenchmarkRun> Predecessors(IEnumerable<BenchmarkRun> runs, IReadOnlyDictionary<long, BenchmarkRun> known)
    {
        var list = runs.ToList();
        var own = list.Select(r => r.Id).ToHashSet();
        var seen = new HashSet<long>();
        var result = new List<BenchmarkRun>();
        foreach (var run in list)
        {
            long? next = run.ReplacesRunId;
            while (next is long id && id != run.Id && seen.Add(id) && known.TryGetValue(id, out var predecessor))
            {
                if (!own.Contains(id)) result.Add(predecessor);
                next = predecessor.ReplacesRunId;
            }
        }

        return result.OrderBy(r => r.StartedAtUtc).ThenBy(r => r.Id).ToList();
    }

    /// <summary>
    /// The summary of <paramref name="members"/>: their candidate attempts, and the re-runs of the members and
    /// their lineage predecessors (which add re-runs, never attempts).
    /// </summary>
    public static ProviderAvailabilitySummary Summary(
        IReadOnlyList<BenchmarkRun> members,
        IReadOnlyDictionary<long, IReadOnlyList<ProviderAttemptRow>> attempts,
        IReadOnlyDictionary<long, BenchmarkRun> lineage)
    {
        var rows = members
            .Where(IsRecorded)
            .SelectMany(r => attempts.TryGetValue(r.Id, out var list) ? list : Array.Empty<ProviderAttemptRow>())
            .ToList();
        var pool = members.Select(r => LineageRow(r, true))
            .Concat(Predecessors(members, lineage).Select(r => LineageRow(r, false)))
            .ToList();
        return BenchmarkProviderAvailability.Summarize(rows, pool);
    }

    /// <summary>
    /// The candidate rollups of <paramref name="runs"/>, the recorded ones: valid attempts, provider errors and
    /// unattributed errors summed, and the rate; all null when no run recorded attempts.
    /// </summary>
    public static (bool Recorded, int? Valid, int? Provider, int? Unattributed, double? Rate) Rollups(IEnumerable<BenchmarkRun> runs)
    {
        var recorded = runs.Where(r => IsRecorded(r) && r.CandidateValidAttemptCount.HasValue).ToList();
        if (recorded.Count == 0) return (false, null, null, null, null);

        int valid = recorded.Sum(r => r.CandidateValidAttemptCount ?? 0);
        int provider = recorded.Sum(r => r.CandidateProviderErrorCount ?? 0);
        int unattributed = recorded.Sum(r => r.CandidateUnattributedErrorCount ?? 0);
        return (true, valid, provider, unattributed, valid > 0 ? provider / (double)valid : null);
    }

    /// <summary>The re-runs of <paramref name="runs"/> and their lineage predecessors.</summary>
    public static ProviderRerunCounts Reruns(IReadOnlyList<BenchmarkRun> runs, IReadOnlyDictionary<long, BenchmarkRun> lineage)
        => BenchmarkProviderAvailability.CountReruns(runs.Select(r => LineageRow(r, true))
            .Concat(Predecessors(runs, lineage).Select(r => LineageRow(r, false))));

    /// <summary>The valid candidate attempts of <paramref name="runs"/>, each as its stratum and 100 for a provider error, else 0.</summary>
    public static List<StratifiedObservation> Observations(
        IEnumerable<BenchmarkRun> runs, IReadOnlyDictionary<long, IReadOnlyList<ProviderAttemptRow>> attempts)
        => runs
            .SelectMany(r => attempts.TryGetValue(r.Id, out var list) ? list : Array.Empty<ProviderAttemptRow>())
            .Where(a => ProviderAttemptOutcomes.IsValid(a.Outcome))
            .Select(a => new StratifiedObservation(
                ChatConsistencyStatistics.AssignStratum(ChatConsistencyMeasures.AsUtc(a.StartedAtUtc)).Index,
                ProviderAttemptOutcomes.IsProvider(a.Outcome) ? 100.0 : 0.0))
            .ToList();

    /// <summary>The candidate attempts of <paramref name="runs"/>.</summary>
    public static List<ProviderAttemptRow> AttemptsOf(
        IEnumerable<BenchmarkRun> runs, IReadOnlyDictionary<long, IReadOnlyList<ProviderAttemptRow>> attempts)
        => runs.SelectMany(r => attempts.TryGetValue(r.Id, out var list) ? list : Array.Empty<ProviderAttemptRow>()).ToList();
}
