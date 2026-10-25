namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Models;

/// <summary>
/// Suggests run groups: clusters of one suite's settled runs that share a full comparability key
/// hash, which is a Tier A set, and that no group holds yet. Read-only; a suggestion becomes a group
/// only when the operator creates it.
/// </summary>
public class BenchmarkRunGroupSuggestionService
{
    /// <summary>The newest runs of the suite that are clustered; older runs are not suggested.</summary>
    internal const int MaxRunsConsidered = 300;

    /// <summary>The most suggestions returned, newest cluster first.</summary>
    internal const int MaxSuggestions = 20;

    private readonly ApplicationDbContext _db;

    public BenchmarkRunGroupSuggestionService(ApplicationDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// The suite's suggested groups. A cluster qualifies with at least two runs that resolve Tier A
    /// together, and is left out when an existing group's members equal it or include it.
    /// </summary>
    public async Task<List<BenchmarkRunGroupSuggestionDto>> SuggestAsync(long suiteId, CancellationToken ct = default)
    {
        var statuses = BenchmarkGroupAnalysisService.PoolableStatuses;

        // Untracked: the answer stubs the key hash reads must never be saved.
        var runs = await _db.BenchmarkRuns
            .AsNoTracking()
            .Where(r => r.BenchmarkSuiteId == suiteId && statuses.Contains(r.Status))
            .OrderByDescending(r => r.StartedAtUtc)
            .ThenByDescending(r => r.Id)
            .Take(MaxRunsConsidered)
            .ToListAsync(ct);

        if (runs.Count < 2) return new List<BenchmarkRunGroupSuggestionDto>();

        await BenchmarkSeriesOrchestrator.HydrateItemRevisionsAsync(_db, runs, ct);

        var clusters = runs
            .GroupBy(BenchmarkComparabilityKey.ComputeKeyHash, StringComparer.Ordinal)
            .Where(c => c.Count() >= 2)
            .Select(c => (KeyHash: c.Key, Runs: c.OrderBy(r => r.Id).ToList()))
            .Where(c => BenchmarkComparabilityKey.Resolve(c.Runs).Tier == BenchmarkComparabilityTier.Replicate)
            .ToList();

        if (clusters.Count == 0) return new List<BenchmarkRunGroupSuggestionDto>();

        var existingGroups = await LoadGroupMembershipsAsync(clusters.SelectMany(c => c.Runs).Select(r => r.Id).ToList(), ct);

        return clusters
            .Where(c =>
            {
                var ids = c.Runs.Select(r => r.Id).ToHashSet();
                return !existingGroups.Any(g => ids.IsSubsetOf(g));
            })
            .Select(c => ToDto(c.KeyHash, c.Runs))
            .OrderByDescending(s => s.LastStartedAtUtc)
            .ThenByDescending(s => s.RunIds[^1])
            .Take(MaxSuggestions)
            .ToList();
    }

    /// <summary>The full member set of every group that holds any of the runs.</summary>
    private async Task<List<HashSet<long>>> LoadGroupMembershipsAsync(List<long> runIds, CancellationToken ct)
    {
        var groupIds = await _db.BenchmarkRunGroupMembers
            .AsNoTracking()
            .Where(m => runIds.Contains(m.BenchmarkRunId))
            .Select(m => m.BenchmarkRunGroupId)
            .Distinct()
            .ToListAsync(ct);

        if (groupIds.Count == 0) return new List<HashSet<long>>();

        var memberships = await _db.BenchmarkRunGroupMembers
            .AsNoTracking()
            .Where(m => groupIds.Contains(m.BenchmarkRunGroupId))
            .Select(m => new { m.BenchmarkRunGroupId, m.BenchmarkRunId })
            .ToListAsync(ct);

        return memberships
            .GroupBy(m => m.BenchmarkRunGroupId)
            .Select(g => g.Select(m => m.BenchmarkRunId).ToHashSet())
            .ToList();
    }

    private static BenchmarkRunGroupSuggestionDto ToDto(string keyHash, IReadOnlyList<BenchmarkRun> runs)
    {
        var newest = runs.OrderByDescending(r => r.StartedAtUtc).ThenByDescending(r => r.Id).First();
        var model = newest.TestedModelSnapshot;
        var indices = runs.Where(r => r.QualityIndex.HasValue).Select(r => (double)r.QualityIndex!.Value).ToList();

        return new BenchmarkRunGroupSuggestionDto
        {
            KeyHash = keyHash,
            RunIds = runs.Select(r => r.Id).OrderBy(id => id).ToList(),
            RunCount = runs.Count,
            Tier = BenchmarkRunGroupTier.Replicate.ToString(),
            TestedModel = new BenchmarkRunGroupSuggestionModelDto
            {
                DisplayName = model?.Label() ?? string.Empty,
                Id = model?.ModelId ?? string.Empty,
                Provider = model?.Provider ?? string.Empty,
                ThinkingLevel = model?.ThinkingLevel,
                ReasoningMode = model?.ReasoningMode
            },
            FirstStartedAtUtc = runs.Min(r => r.StartedAtUtc),
            LastStartedAtUtc = runs.Max(r => r.StartedAtUtc),
            SeriesIds = runs
                .Where(r => r.RunSeriesId.HasValue)
                .Select(r => r.RunSeriesId!.Value)
                .Distinct()
                .OrderBy(id => id)
                .ToList(),
            MeanQualityIndex = indices.Count > 0 ? indices.Average() : null
        };
    }
}
