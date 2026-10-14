namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.ChatConsistency;

/// <summary>
/// Finds the baseline model's latest result that Model Comparison can chart beside a model batch's
/// members: a run on the batch's suite, or a battery run of the batch's battery definition. A result
/// is the baseline model when its tested snapshot has the baseline's model-axis key
/// (<see cref="ChatConsistencyComparability.ModelAxisKey(SystemAiConfigurationSnapshot?)"/>).
/// Read-only.
/// </summary>
public class BenchmarkBaselineModelSourceFinder
{
    /// <summary>The baseline model's newest runs on the suite checked for comparability, at most.</summary>
    public const int MaxSuiteCandidates = 10;

    private static readonly BenchmarkRunStatus[] CompletedStatuses =
    {
        BenchmarkRunStatus.Completed,
        BenchmarkRunStatus.CompletedWithLimits,
        BenchmarkRunStatus.CompletedWithErrors
    };

    private readonly ApplicationDbContext _db;

    public BenchmarkBaselineModelSourceFinder(ApplicationDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// The newest finished run on <paramref name="suiteId"/> with the baseline's model-axis key whose
    /// must-match signature is the one <see cref="BenchmarkCrossModelComparability.Resolve"/> picks for
    /// the members; at most <see cref="MaxSuiteCandidates"/> runs are tried. Null when none qualifies.
    /// </summary>
    /// <param name="memberRuns">Each member's finished runs: one cross-model entry per member.</param>
    /// <param name="excludedRunIds">Runs never offered, such as the batch's own.</param>
    public async Task<BenchmarkBaselineModelSourceDto?> FindSuiteRunAsync(
        string baselineAxisKey,
        long suiteId,
        IReadOnlyList<IReadOnlyList<BenchmarkRun>> memberRuns,
        IReadOnlyCollection<long> excludedRunIds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(baselineAxisKey);
        ArgumentNullException.ThrowIfNull(memberRuns);

        var entries = memberRuns
            .Where(runs => runs != null && runs.Count > 0)
            .Select((runs, i) => new BenchmarkCrossModelEntry { Key = i.ToString(CultureInfo.InvariantCulture), Runs = runs })
            .ToList();
        if (entries.Count == 0) return null;

        await BenchmarkSeriesOrchestrator.HydrateItemRevisionsAsync(_db, entries.SelectMany(e => e.Runs).Distinct().ToList(), ct);
        string signature = BenchmarkCrossModelComparability.Resolve(entries).BaselineSignature;
        if (string.IsNullOrEmpty(signature)) return null;

        var snapshotIds = await _db.BenchmarkRuns
            .AsNoTracking()
            .Where(r => r.BenchmarkSuiteId == suiteId && CompletedStatuses.Contains(r.Status))
            .Select(r => r.TestedModelSnapshotId)
            .Distinct()
            .ToListAsync(ct);
        var matching = await MatchingSnapshotIdsAsync(snapshotIds, baselineAxisKey, ct);
        if (matching.Count == 0) return null;

        var excluded = (excludedRunIds ?? Array.Empty<long>()).ToList();
        var candidates = await _db.BenchmarkRuns
            .AsNoTracking()
            .Where(r => r.BenchmarkSuiteId == suiteId
                        && CompletedStatuses.Contains(r.Status)
                        && matching.Contains(r.TestedModelSnapshotId)
                        && !excluded.Contains(r.Id))
            .OrderByDescending(r => r.StartedAtUtc)
            .ThenByDescending(r => r.Id)
            .Take(MaxSuiteCandidates)
            .ToListAsync(ct);
        if (candidates.Count == 0) return null;

        await BenchmarkSeriesOrchestrator.HydrateItemRevisionsAsync(_db, candidates, ct);
        var chosen = candidates.FirstOrDefault(r =>
            string.Equals(BenchmarkCrossModelComparability.MustMatchSignature(r), signature, StringComparison.Ordinal));
        if (chosen == null) return null;

        return new BenchmarkBaselineModelSourceDto
        {
            RunId = chosen.Id,
            Label = $"{ModelName(chosen.TestedModelSnapshot)}, run #{chosen.Id.ToString(CultureInfo.InvariantCulture)}",
            StartedAtUtc = chosen.StartedAtUtc
        };
    }

    /// <summary>
    /// The newest battery run that the leaderboard ranks in the members' battery definition and
    /// comparability class, whose result's newest member run has the baseline's model-axis key. The
    /// members' definition and class are the ones most of their latest ranked analyses share. Null
    /// when none qualifies.
    /// </summary>
    public async Task<BenchmarkBaselineModelSourceDto?> FindBatteryRunAsync(
        string baselineAxisKey,
        IReadOnlyCollection<long> memberBatteryRunIds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(baselineAxisKey);
        var memberIds = (memberBatteryRunIds ?? Array.Empty<long>()).Distinct().ToList();
        if (memberIds.Count == 0) return null;

        var memberHeads = Latest(await AnalysisHeads(_db.BenchmarkBatteryAnalyses
                .Where(a => memberIds.Contains(a.BenchmarkBatteryRunId)))
            .ToListAsync(ct));
        var target = memberIds
            .Select(id => memberHeads.FirstOrDefault(a => a.BatteryRunId == id))
            .Where(a => a != null && BenchmarkBatteryLeaderboardService.IsRankable(a.Complete, a.ClassSha256))
            .Select(a => (DefinitionSha256: a!.DefinitionSha256, ClassSha256: a.ClassSha256!))
            .GroupBy(k => k)
            .OrderByDescending(g => g.Count())
            .Select(g => (Key: g.Key, Found: true))
            .FirstOrDefault();
        if (!target.Found) return null;

        string definition = target.Key.DefinitionSha256;
        string comparabilityClass = target.Key.ClassSha256;

        var candidates = Latest(await AnalysisHeads(_db.BenchmarkBatteryAnalyses
                    .Where(a => a.DefinitionSha256 == definition && !memberIds.Contains(a.BenchmarkBatteryRunId)))
                .ToListAsync(ct))
            .Where(a => BenchmarkBatteryLeaderboardService.IsRankable(a.Complete, a.ClassSha256)
                        && string.Equals(a.ClassSha256, comparabilityClass, StringComparison.Ordinal))
            .ToList();
        if (candidates.Count == 0) return null;

        var candidateIds = candidates.Select(a => a.BatteryRunId).ToList();
        var startedAt = await _db.BenchmarkBatteryRuns
            .AsNoTracking()
            .Where(b => candidateIds.Contains(b.Id))
            .Select(b => new { b.Id, b.StartedAtUtc })
            .ToDictionaryAsync(b => b.Id, b => b.StartedAtUtc, ct);

        var memberRunIds = candidates
            .ToDictionary(a => a.BatteryRunId, a => BenchmarkBatteryLeaderboardService.ReadMemberRunIds(
                new BenchmarkBatteryAnalysis { MemberRunIdsJson = a.MemberRunIdsJson }));
        var allRunIds = memberRunIds.Values.SelectMany(ids => ids).Distinct().ToList();
        var runs = await _db.BenchmarkRuns
            .AsNoTracking()
            .Where(r => allRunIds.Contains(r.Id))
            .Select(r => new { r.Id, r.StartedAtUtc, r.TestedModelSnapshotId })
            .ToListAsync(ct);
        var runById = runs.ToDictionary(r => r.Id);

        var snapshotIds = runs.Select(r => r.TestedModelSnapshotId).Distinct().ToList();
        var snapshots = await _db.SystemAiConfigurationSnapshots
            .AsNoTracking()
            .Where(s => snapshotIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, ct);

        foreach (var analysis in candidates
                     .Where(a => startedAt.ContainsKey(a.BatteryRunId))
                     .OrderByDescending(a => startedAt[a.BatteryRunId])
                     .ThenByDescending(a => a.BatteryRunId))
        {
            var newest = memberRunIds[analysis.BatteryRunId]
                .Where(runById.ContainsKey)
                .Select(id => runById[id])
                .OrderByDescending(r => r.StartedAtUtc)
                .ThenByDescending(r => r.Id)
                .FirstOrDefault();
            if (newest == null || !snapshots.TryGetValue(newest.TestedModelSnapshotId, out var snapshot)) continue;
            if (!string.Equals(ChatConsistencyComparability.ModelAxisKey(snapshot), baselineAxisKey, StringComparison.Ordinal)) continue;

            return new BenchmarkBaselineModelSourceDto
            {
                BatteryRunId = analysis.BatteryRunId,
                Label = $"{ModelName(snapshot)}, battery run #{analysis.BatteryRunId.ToString(CultureInfo.InvariantCulture)}",
                StartedAtUtc = startedAt[analysis.BatteryRunId]
            };
        }

        return null;
    }

    /// <summary>The snapshots among <paramref name="snapshotIds"/> with the baseline's model-axis key.</summary>
    private async Task<List<long>> MatchingSnapshotIdsAsync(IReadOnlyList<long> snapshotIds, string baselineAxisKey, CancellationToken ct)
    {
        if (snapshotIds.Count == 0) return new List<long>();

        var snapshots = await _db.SystemAiConfigurationSnapshots
            .AsNoTracking()
            .Where(s => snapshotIds.Contains(s.Id))
            .ToListAsync(ct);
        return snapshots
            .Where(s => string.Equals(ChatConsistencyComparability.ModelAxisKey(s), baselineAxisKey, StringComparison.Ordinal))
            .Select(s => s.Id)
            .ToList();
    }

    private static IQueryable<AnalysisHead> AnalysisHeads(IQueryable<BenchmarkBatteryAnalysis> analyses)
        => analyses
            .AsNoTracking()
            .Select(a => new AnalysisHead
            {
                Id = a.Id,
                BatteryRunId = a.BenchmarkBatteryRunId,
                ComputedAtUtc = a.ComputedAtUtc,
                DefinitionSha256 = a.DefinitionSha256,
                ClassSha256 = a.ComparabilityClassSha256,
                Complete = a.Complete,
                MemberRunIdsJson = a.MemberRunIdsJson
            });

    /// <summary>Each battery run's latest analysis, as the leaderboard ranks it.</summary>
    private static List<AnalysisHead> Latest(IEnumerable<AnalysisHead> heads)
        => heads
            .GroupBy(a => a.BatteryRunId)
            .Select(g => g.OrderByDescending(a => a.ComputedAtUtc).ThenByDescending(a => a.Id).First())
            .ToList();

    private static string ModelName(SystemAiConfigurationSnapshot? snapshot)
        => snapshot.Label() is { Length: > 0 } label ? label.Trim() : "The baseline model";

    /// <summary>The columns of a battery analysis the finder reads.</summary>
    private sealed class AnalysisHead
    {
        public long Id { get; set; }
        public long BatteryRunId { get; set; }
        public DateTime ComputedAtUtc { get; set; }
        public string DefinitionSha256 { get; set; } = string.Empty;
        public string? ClassSha256 { get; set; }
        public bool Complete { get; set; }
        public string MemberRunIdsJson { get; set; } = string.Empty;
    }
}
