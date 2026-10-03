namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Models;

/// <summary>
/// What a battery run was measured with, as its cards, report header and leaderboard row show it:
/// the tested model's settings, the graders, the scoring profile and the response style.
/// </summary>
public sealed class BenchmarkBatteryRunIdentity
{
    public string? TestedProvider { get; init; }
    public string? TestedModelId { get; init; }
    public string? TestedThinkingLevel { get; init; }
    public string? TestedReasoningMode { get; init; }
    public string? TestedServiceTier { get; init; }
    public string? AssessorLabel { get; init; }
    public string? AssessorProvider { get; init; }
    public string? AssessorThinkingLevel { get; init; }
    public string? AssessorReasoningMode { get; init; }
    public string? CoAssessorLabel { get; init; }
    public string? CoAssessorProvider { get; init; }
    public string? CoAssessorThinkingLevel { get; init; }
    public string? CoAssessorReasoningMode { get; init; }
    public string? ScoringProfileName { get; init; }
    public bool VerboseMode { get; init; }
}

/// <summary>The leaderboard of one definition hash, reduced to what a battery card shows.</summary>
public sealed class BenchmarkBatteryLeaderboardSummary
{
    /// <summary>Battery runs whose latest analysis the leaderboard ranks.</summary>
    public int RankedResultCount { get; init; }

    /// <summary>The newest analysis of any battery run with the hash; null when there is none.</summary>
    public DateTime? LatestAnalysisAtUtc { get; init; }
}

/// <summary>
/// The parts of the battery leaderboard that other readers share: the keys that distinguish its
/// comparability classes, the identity of a battery run, and the per-definition ranked-result counts.
/// Read-only over stored rows.
/// </summary>
public class BenchmarkBatteryLeaderboardService
{
    private readonly ApplicationDbContext _db;

    public BenchmarkBatteryLeaderboardService(ApplicationDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// The leaderboard's ranking rule: a complete analysis with a comparability class and an Overall
    /// Index. A complete analysis always carries its headline (Statistical Method M4).
    /// </summary>
    public static bool IsRanked(bool complete, string? comparabilityClassSha256, double? overallIndex)
        => IsRankable(complete, comparabilityClassSha256) && overallIndex.HasValue;

    /// <summary><see cref="IsRanked"/> on the columns alone, without reading the result JSON.</summary>
    public static bool IsRankable(bool complete, string? comparabilityClassSha256)
        => complete && !string.IsNullOrEmpty(comparabilityClassSha256);

    /// <summary>The usable member run ids an analysis was computed over; empty when unreadable.</summary>
    public static long[] ReadMemberRunIds(BenchmarkBatteryAnalysis analysis)
    {
        try
        {
            return JsonSerializer.Deserialize<long[]>(analysis.MemberRunIdsJson ?? "[]") ?? Array.Empty<long>();
        }
        catch (JsonException)
        {
            return Array.Empty<long>();
        }
    }

    /// <summary>
    /// Per definition hash (lower case), the leaderboard's ranked-result count and newest analysis
    /// time, in one query over the analysis columns: the latest analysis of each battery run, ranked
    /// by <see cref="IsRankable"/>. Hashes without an analysis are absent.
    /// </summary>
    public async Task<Dictionary<string, BenchmarkBatteryLeaderboardSummary>> LoadSummariesAsync(
        IEnumerable<string> definitionHashes,
        CancellationToken ct)
    {
        var hashes = definitionHashes
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Select(h => h.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();

        if (hashes.Count == 0) return new Dictionary<string, BenchmarkBatteryLeaderboardSummary>(StringComparer.Ordinal);

        var heads = await _db.BenchmarkBatteryAnalyses
            .AsNoTracking()
            .Where(a => hashes.Contains(a.DefinitionSha256))
            .Select(a => new
            {
                a.Id,
                a.BenchmarkBatteryRunId,
                a.DefinitionSha256,
                a.ComputedAtUtc,
                a.Complete,
                a.ComparabilityClassSha256
            })
            .ToListAsync(ct);

        return heads
            .GroupBy(a => a.DefinitionSha256, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var latest = g
                        .GroupBy(a => a.BenchmarkBatteryRunId)
                        .Select(run => run.OrderByDescending(a => a.ComputedAtUtc).ThenByDescending(a => a.Id).First())
                        .ToList();

                    return new BenchmarkBatteryLeaderboardSummary
                    {
                        RankedResultCount = latest.Count(a => IsRankable(a.Complete, a.ComparabilityClassSha256)),
                        LatestAnalysisAtUtc = latest.Max(a => a.ComputedAtUtc)
                    };
                },
                StringComparer.Ordinal);
    }

    /// <summary>
    /// The identity of each battery run: read from the member run named in
    /// <paramref name="identityRunIds"/> (its snapshots, as a run DTO shows them), else from the
    /// stored start request and the configurations it names.
    /// </summary>
    public async Task<Dictionary<long, BenchmarkBatteryRunIdentity>> LoadIdentitiesAsync(
        IReadOnlyList<BenchmarkBatteryRun> batteryRuns,
        IReadOnlyDictionary<long, long> identityRunIds,
        CancellationToken ct)
    {
        var identities = new Dictionary<long, BenchmarkBatteryRunIdentity>();
        if (batteryRuns.Count == 0) return identities;

        var runIds = batteryRuns
            .Where(b => identityRunIds.ContainsKey(b.Id))
            .Select(b => identityRunIds[b.Id])
            .Distinct()
            .ToList();

        var runs = runIds.Count == 0
            ? new Dictionary<long, IdentityRun>()
            : await _db.BenchmarkRuns
                .AsNoTracking()
                .Where(r => runIds.Contains(r.Id))
                .Select(r => new IdentityRun
                {
                    Id = r.Id,
                    TestedModelSnapshotId = r.TestedModelSnapshotId,
                    AssessorModelSnapshotId = r.AssessorModelSnapshotId,
                    CoAssessorModelSnapshotId = r.CoAssessorModelSnapshotId,
                    ScoringProfileName = r.ScoringProfile != null ? r.ScoringProfile.Name : null,
                    CandidatePromptOptionsJson = r.CandidatePromptOptionsJson
                })
                .ToDictionaryAsync(r => r.Id, ct);

        var snapshotIds = runs.Values
            .SelectMany(r => new long?[] { r.TestedModelSnapshotId, r.AssessorModelSnapshotId, r.CoAssessorModelSnapshotId })
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var snapshots = snapshotIds.Count == 0
            ? new Dictionary<long, SystemAiConfigurationSnapshot>()
            : await _db.SystemAiConfigurationSnapshots
                .AsNoTracking()
                .Where(s => snapshotIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, ct);

        SystemAiConfigurationSnapshot? Snapshot(long? id)
            => id.HasValue && snapshots.TryGetValue(id.Value, out var s) ? s : null;

        var fallback = new List<(long BatteryRunId, StartBenchmarkRunRequest? Request)>();
        foreach (var batteryRun in batteryRuns)
        {
            if (identityRunIds.TryGetValue(batteryRun.Id, out long runId) && runs.TryGetValue(runId, out var run))
            {
                var tested = Snapshot(run.TestedModelSnapshotId);
                var assessorSnapshot = Snapshot(run.AssessorModelSnapshotId);
                var coAssessorSnapshot = Snapshot(run.CoAssessorModelSnapshotId);
                identities[batteryRun.Id] = new BenchmarkBatteryRunIdentity
                {
                    TestedProvider = tested?.Provider,
                    TestedModelId = tested?.ModelId,
                    TestedThinkingLevel = tested?.ThinkingLevel,
                    TestedReasoningMode = tested?.ReasoningMode,
                    TestedServiceTier = tested?.ServiceTier,
                    AssessorLabel = assessorSnapshot.Label(),
                    AssessorProvider = assessorSnapshot?.Provider,
                    AssessorThinkingLevel = assessorSnapshot?.ThinkingLevel,
                    AssessorReasoningMode = assessorSnapshot?.ReasoningMode,
                    CoAssessorLabel = coAssessorSnapshot.Label(),
                    CoAssessorProvider = coAssessorSnapshot?.Provider,
                    CoAssessorThinkingLevel = coAssessorSnapshot?.ThinkingLevel,
                    CoAssessorReasoningMode = coAssessorSnapshot?.ReasoningMode,
                    ScoringProfileName = run.ScoringProfileName,
                    VerboseMode = BenchmarkCandidatePromptOptions.FromJson(run.CandidatePromptOptionsJson).VerboseMode
                };
            }
            else
            {
                fallback.Add((batteryRun.Id, BenchmarkBatteryOrchestrator.DeserializeRequest(batteryRun)));
            }
        }

        if (fallback.Count == 0) return identities;

        var configIds = fallback
            .Where(f => f.Request != null)
            .SelectMany(f => new long?[]
            {
                f.Request!.TestedModelConfigurationId,
                f.Request.AssessorModelConfigurationId,
                f.Request.CoAssessorModelConfigurationId
            })
            .Where(id => id.HasValue && id.Value > 0)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        var configs = configIds.Count == 0
            ? new Dictionary<long, SystemAiApiConfiguration>()
            : await _db.SystemAiApiConfigurations
                .AsNoTracking()
                .Where(c => configIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, ct);

        var profileIds = fallback
            .Where(f => f.Request?.ScoringProfileId != null)
            .Select(f => f.Request!.ScoringProfileId!.Value)
            .Distinct()
            .ToList();

        var profileNames = profileIds.Count == 0
            ? new Dictionary<long, string>()
            : await _db.BenchmarkScoringProfiles
                .AsNoTracking()
                .Where(p => profileIds.Contains(p.Id))
                .Select(p => new { p.Id, p.Name })
                .ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        SystemAiApiConfiguration? Config(long? id)
            => id.HasValue && configs.TryGetValue(id.Value, out var c) ? c : null;

        foreach (var (batteryRunId, request) in fallback)
        {
            var tested = Config(request?.TestedModelConfigurationId);
            var assessor = Config(request?.AssessorModelConfigurationId);
            var coAssessor = Config(request?.CoAssessorModelConfigurationId);

            identities[batteryRunId] = new BenchmarkBatteryRunIdentity
            {
                TestedProvider = tested?.Provider,
                TestedModelId = tested?.ModelId,
                TestedThinkingLevel = tested?.ThinkingLevel,
                TestedReasoningMode = tested?.ReasoningMode,
                TestedServiceTier = tested?.ServiceTier,
                AssessorLabel = assessor == null ? null : assessor.DisplayName ?? assessor.ModelId,
                AssessorProvider = assessor?.Provider,
                AssessorThinkingLevel = assessor?.ThinkingLevel,
                AssessorReasoningMode = assessor?.ReasoningMode,
                CoAssessorLabel = coAssessor == null ? null : coAssessor.DisplayName ?? coAssessor.ModelId,
                CoAssessorProvider = coAssessor?.Provider,
                CoAssessorThinkingLevel = coAssessor?.ThinkingLevel,
                CoAssessorReasoningMode = coAssessor?.ReasoningMode,
                ScoringProfileName = request?.ScoringProfileId is long profileId && profileNames.TryGetValue(profileId, out var name)
                    ? name
                    : null,
                VerboseMode = request?.VerboseMode ?? false
            };
        }

        return identities;
    }

    /// <summary>The columns of a member run that <see cref="LoadIdentitiesAsync"/> reads.</summary>
    private sealed class IdentityRun
    {
        public long Id { get; set; }
        public long TestedModelSnapshotId { get; set; }
        public long AssessorModelSnapshotId { get; set; }
        public long? CoAssessorModelSnapshotId { get; set; }
        public string? ScoringProfileName { get; set; }
        public string? CandidatePromptOptionsJson { get; set; }
    }

    /// <summary>
    /// Per class, the must-match comparability keys whose values (per suite) differ from those of at
    /// least one other class, read from one representative analysis per class, in key order.
    /// </summary>
    public async Task<Dictionary<string, List<string>>> DistinguishingKeysAsync(
        IReadOnlyDictionary<string, BenchmarkBatteryAnalysis> representatives,
        CancellationToken ct)
    {
        var runIdsByClass = representatives.ToDictionary(p => p.Key, p => ReadMemberRunIds(p.Value));
        var allIds = runIdsByClass.Values.SelectMany(ids => ids).Distinct().ToList();

        var runs = await _db.BenchmarkRuns
            .AsNoTracking()
            .Where(r => allIds.Contains(r.Id))
            .ToListAsync(ct);
        await BenchmarkSeriesOrchestrator.HydrateItemRevisionsAsync(_db, runs, ct);
        var runById = runs.ToDictionary(r => r.Id);

        var keyOrder = new List<string>();
        var valuesByClass = new Dictionary<string, Dictionary<string, SortedSet<string>>>(StringComparer.Ordinal);

        foreach (var (cls, ids) in runIdsByClass)
        {
            var values = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            foreach (long runId in ids)
            {
                if (!runById.TryGetValue(runId, out var run)) continue;

                long? suiteId = run.BenchmarkSuiteIdUsed ?? run.BenchmarkSuiteId;
                foreach (var key in BenchmarkCrossModelComparability.MustMatchKeys(run))
                {
                    if (!keyOrder.Contains(key.Name)) keyOrder.Add(key.Name);
                    if (!values.TryGetValue(key.Name, out var set))
                    {
                        set = new SortedSet<string>(StringComparer.Ordinal);
                        values[key.Name] = set;
                    }
                    set.Add($"{suiteId}:{key.Value}");
                }
            }
            valuesByClass[cls] = values;
        }

        var distinguishing = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (cls, values) in valuesByClass)
        {
            distinguishing[cls] = keyOrder
                .Where(name => valuesByClass
                    .Where(other => other.Key != cls)
                    .Any(other => !SetOf(values, name).SetEquals(SetOf(other.Value, name))))
                .ToList();
        }

        return distinguishing;

        static SortedSet<string> SetOf(Dictionary<string, SortedSet<string>> values, string name)
            => values.TryGetValue(name, out var set) ? set : new SortedSet<string>(StringComparer.Ordinal);
    }
}
