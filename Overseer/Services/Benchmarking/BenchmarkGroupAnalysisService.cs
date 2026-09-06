namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MobileGnollHackLogger.Data;
using Overseer.Services;

/// <summary>
/// Loads an analysis group, computes its statistics and persists the result.
///
/// <para>This is the only place the multi-run statistics meet the database. Everything scientific
/// happens in <see cref="BenchmarkGroupStatistics"/>, which is pure; everything about *which* runs
/// may be averaged happens in <see cref="BenchmarkComparabilityKey"/>, which is also pure. This
/// class does the I/O and, importantly, the one thing neither of them can: it refuses to pool a set
/// the tier resolution says must not be pooled.</para>
///
/// <para>Admin-initiated only, and it makes no AI calls. A group analysis over twenty runs is
/// arithmetic and completes in well under a second.</para>
/// </summary>
public class BenchmarkGroupAnalysisService
{
    private readonly ApplicationDbContext _db;
    private readonly ModelPricingService? _pricingService;
    private readonly ILogger<BenchmarkGroupAnalysisService> _logger;

    public const string CandidateRole = "Candidate";
    public const string AssessorRole = "Assessor";
    public const string ClaimVerifierRole = "Claim verifier";

    public BenchmarkGroupAnalysisService(
        ApplicationDbContext db,
        ILogger<BenchmarkGroupAnalysisService> logger,
        ModelPricingService? pricingService = null)
    {
        _db = db;
        _logger = logger;
        _pricingService = pricingService;
    }

    /// <summary>Everything a caller needs to render or report on one group, loaded once.</summary>
    public sealed record LoadedGroup
    {
        public BenchmarkRunGroup Group { get; init; } = default!;
        public BenchmarkSuite? Suite { get; init; }
        public IReadOnlyList<BenchmarkRun> Runs { get; init; } = Array.Empty<BenchmarkRun>();
        public BenchmarkComparabilityResult Comparability { get; init; } = new();
    }

    /// <summary>
    /// Loads a group with its members' answers and re-resolves the comparability tier from the runs
    /// as they are now. Re-resolving rather than trusting the stored tier is deliberate: a member
    /// may have been added since, and a stale Tier A would be exactly the silent failure the tier
    /// model exists to prevent.
    /// </summary>
    public async Task<LoadedGroup?> LoadGroupAsync(long groupId, CancellationToken ct = default)
    {
        var group = await _db.BenchmarkRunGroups
            .Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.Id == groupId, ct);

        if (group == null) return null;

        var runIds = group.Members.Select(m => m.BenchmarkRunId).ToList();

        var runs = await _db.BenchmarkRuns
            .Include(r => r.Answers)
            .Where(r => runIds.Contains(r.Id))
            .OrderBy(r => r.Id)
            .ToListAsync(ct);

        BenchmarkSuite? suite = null;
        if (group.BenchmarkSuiteId.HasValue)
        {
            suite = await _db.BenchmarkSuites
                .Include(s => s.Questions)
                .FirstOrDefaultAsync(s => s.Id == group.BenchmarkSuiteId.Value, ct);
        }

        return new LoadedGroup
        {
            Group = group,
            Suite = suite,
            Runs = runs,
            Comparability = BenchmarkComparabilityKey.Resolve(runs)
        };
    }

    /// <summary>
    /// Computes the statistics for a loaded group and persists them.
    ///
    /// Refuses when the set is below Tier B, and refuses to pool a Tier C set — such a set is two
    /// conditions, and the honest operation on it is a comparison, not an average.
    /// </summary>
    public async Task<(BenchmarkGroupAnalysis? Analysis, BenchmarkGroupStatisticsResult? Result, string? Error)>
        AnalyseAsync(long groupId, string? computedByUserId, long? comparedWithGroupId = null, CancellationToken ct = default)
    {
        var loaded = await LoadGroupAsync(groupId, ct);
        if (loaded == null)
        {
            return (null, null, "Group not found.");
        }

        if (loaded.Suite == null)
        {
            return (null, null, "The group is not bound to a suite, so its items cannot be identified.");
        }

        if (loaded.Runs.Count < 2)
        {
            return (null, null, "A group analysis needs at least two member runs.");
        }

        var tier = loaded.Comparability.Tier;

        if (tier == BenchmarkComparabilityTier.NotComparable)
        {
            return (null, null,
                "The members are not comparable, so no aggregate over them means anything. " +
                loaded.Comparability.Explanation);
        }

        if (tier == BenchmarkComparabilityTier.CrossCondition)
        {
            return (null, null,
                "This is a cross-condition (Tier C) set: one instrument key was deliberately moved across its members, " +
                "so a pooled index would average a before and an after into a number describing neither. " +
                "Split it into one group per condition and compare them. " +
                loaded.Comparability.Explanation);
        }

        var options = BenchmarkGroupStatisticsOptions.FromComparability(loaded.Comparability);
        var costs = await ResolveCostsAsync(loaded.Runs);

        var result = BenchmarkGroupStatistics.Compute(
            loaded.Suite,
            loaded.Suite.Questions.ToList(),
            loaded.Runs,
            costs,
            options);

        var newest = loaded.Runs.OrderByDescending(r => r.StartedAtUtc).First();

        // The paired comparison — the T15 use case. Computed here rather than on demand so the
        // stored analysis is self-contained: the baseline group's membership may change later, and
        // a report must keep describing the comparison that was actually run.
        //
        // The named group is the *baseline* and this one the treatment, so a positive mean
        // difference means this group scored higher. A comparison that cannot be computed — the
        // baseline is gone, or is itself unanalysable — is not an error for this analysis: the
        // group's own statistics are still valid, and the comparison is simply absent.
        string? comparisonJson = null;
        if (comparedWithGroupId.HasValue && comparedWithGroupId.Value != groupId)
        {
            var baseline = await ComputeForComparisonAsync(comparedWithGroupId.Value, ct);
            if (baseline != null)
            {
                var comparison = BenchmarkGroupStatistics.Compare(baseline, result);
                comparisonJson = JsonSerializer.Serialize(comparison);
            }
            else
            {
                _logger.LogInformation(
                    "Group {GroupId} was analysed without a comparison: baseline group {BaselineId} could not be computed.",
                    groupId, comparedWithGroupId.Value);
            }
        }

        var analysis = new BenchmarkGroupAnalysis
        {
            ComparisonJson = comparisonJson,
            BenchmarkRunGroupId = loaded.Group.Id,
            ComputedAtUtc = DateTime.UtcNow,
            MemberRunIdsJson = JsonSerializer.Serialize(result.RunIds),
            RunCount = result.RunCount,
            ResultJson = JsonSerializer.Serialize(result),
            TierAtComputation = (BenchmarkRunGroupTier)tier,
            HarnessVersion = newest.HarnessVersion,
            ScoringMethodVersion = newest.ScoringMethodVersion,
            ComparedWithGroupId = comparedWithGroupId,
            ComputedByUserId = string.IsNullOrEmpty(computedByUserId) ? null : computedByUserId
        };

        // Keep the group's own tier and reasons in step with what was just resolved, so the list
        // view and the analysis can never disagree about what kind of set this is.
        loaded.Group.Tier = (BenchmarkRunGroupTier)tier;
        loaded.Group.ComparabilityKeyHash = loaded.Comparability.ComparabilityKeyHash;
        loaded.Group.TierReasonsJson = SerialiseTierReasons(loaded.Comparability);
        loaded.Group.ModifiedAtUtc = DateTime.UtcNow;

        _db.BenchmarkGroupAnalyses.Add(analysis);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Computed multi-run analysis {AnalysisId} for group {GroupId} over {RunCount} runs at tier {Tier}.",
            analysis.Id, loaded.Group.Id, result.RunCount, tier);

        return (analysis, result, null);
    }

    /// <summary>
    /// The statistics for a group, computed for use as the baseline half of a comparison and
    /// <b>not</b> persisted — the comparison is stored on the treatment side's analysis, and writing
    /// a second analysis row here would make the baseline group look freshly analysed when nobody
    /// asked it to be.
    ///
    /// <para>Returns null when the group cannot be pooled, for exactly the reasons
    /// <see cref="AnalyseAsync"/> refuses: a Tier C set is two conditions, and a comparison against
    /// the average of two conditions describes neither.</para>
    /// </summary>
    private async Task<BenchmarkGroupStatisticsResult?> ComputeForComparisonAsync(long groupId, CancellationToken ct)
    {
        var loaded = await LoadGroupAsync(groupId, ct);
        if (loaded?.Suite == null || loaded.Runs.Count < 2) return null;

        var tier = loaded.Comparability.Tier;
        if (tier == BenchmarkComparabilityTier.NotComparable
            || tier == BenchmarkComparabilityTier.CrossCondition)
        {
            return null;
        }

        var options = BenchmarkGroupStatisticsOptions.FromComparability(loaded.Comparability);
        var costs = await ResolveCostsAsync(loaded.Runs);

        return BenchmarkGroupStatistics.Compute(
            loaded.Suite,
            loaded.Suite.Questions.ToList(),
            loaded.Runs,
            costs,
            options);
    }

    /// <summary>The most recent stored analysis for a group, or null when none has been computed.</summary>
    public Task<BenchmarkGroupAnalysis?> GetLatestAnalysisAsync(long groupId, CancellationToken ct = default)
        => _db.BenchmarkGroupAnalyses
            .Where(a => a.BenchmarkRunGroupId == groupId)
            .OrderByDescending(a => a.ComputedAtUtc)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// True when the group's membership has changed since the analysis was computed. A stale
    /// analysis is not wrong — it is a correct statement about a different set of runs — so it is
    /// badged rather than discarded.
    /// </summary>
    public static bool IsStale(BenchmarkRunGroup group, BenchmarkGroupAnalysis? analysis)
    {
        if (analysis == null) return false;

        long[] analysed;
        try
        {
            analysed = JsonSerializer.Deserialize<long[]>(analysis.MemberRunIdsJson) ?? Array.Empty<long>();
        }
        catch (JsonException)
        {
            return true;
        }

        var current = group.Members.Select(m => m.BenchmarkRunId).OrderBy(id => id).ToArray();
        return !analysed.OrderBy(id => id).SequenceEqual(current);
    }

    /// <summary>Deserialises a stored result back into the record the report builder consumes.</summary>
    public static BenchmarkGroupStatisticsResult? DeserialiseResult(BenchmarkGroupAnalysis? analysis)
    {
        if (analysis == null || string.IsNullOrWhiteSpace(analysis.ResultJson)) return null;
        try
        {
            return JsonSerializer.Deserialize<BenchmarkGroupStatisticsResult>(analysis.ResultJson);
        }
        catch (JsonException ex)
        {
            // A malformed blob costs one report, never the group.
            System.Diagnostics.Debug.WriteLine(ex.Message);
            return null;
        }
    }

    public static string SerialiseTierReasons(BenchmarkComparabilityResult comparability)
        => JsonSerializer.Serialize(new
        {
            tier = comparability.Tier.ToString(),
            poolingPermitted = comparability.PoolingPermitted,
            speedDegraded = comparability.SpeedAggregatesDegraded,
            costDegraded = comparability.CostAggregatesDegraded,
            explanation = comparability.Explanation,
            matchedKeys = comparability.MatchedKeys,
            differences = comparability.Differences.Select(d => new
            {
                name = d.Name,
                kind = d.Kind.ToString(),
                description = d.Describe(),
                variants = d.Variants.Select(v => new { value = v.Value, runIds = v.RunIds })
            })
        });

    /// <summary>
    /// Per-run, per-role costs for the group, resolved from each run's own pricing snapshot.
    ///
    /// The arithmetic mirrors the single-run report's Estimated Cost block exactly, including the
    /// long-context buckets and the *served* service tier rather than the requested one, so a group
    /// total is the sum of the figures an operator already read on the individual runs. A run whose
    /// pricing cannot be resolved contributes nothing and is simply absent — an unknown cost is
    /// reported as unknown, never as zero.
    /// </summary>
    private async Task<List<BenchmarkGroupRunCost>> ResolveCostsAsync(IReadOnlyList<BenchmarkRun> runs)
    {
        var costs = new List<BenchmarkGroupRunCost>();
        if (_pricingService == null) return costs;

        foreach (var run in runs)
        {
            BenchmarkRunPricing pricing;
            try
            {
                pricing = await _pricingService.ResolveForRunAsync(run);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not resolve pricing for benchmark run {RunId}; it is omitted from the group cost.", run.Id);
                continue;
            }

            if (pricing.Candidate == null) continue;

            var byRole = new Dictionary<string, double>(StringComparer.Ordinal);

            string? servedTier = BenchmarkRunFinalizer.ResolveServedServiceTier(run.Answers);
            decimal candidate = ModelPricingService.ComputeCostFromTotals(
                pricing.Candidate,
                run.TotalInputTokens, run.TotalOutputTokens,
                run.TotalCacheReadTokens, run.TotalCacheCreationTokens,
                run.TotalLongContextInputTokens, run.TotalLongContextOutputTokens,
                run.TotalLongContextCacheReadTokens, run.TotalLongContextCacheCreationTokens,
                actualServiceTier: servedTier,
                requestedServiceTier: run.TestedModelServiceTierUsed);
            byRole[CandidateRole] = (double)candidate;

            bool hasAssessor = run.TotalAssessmentInputTokens > 0 || run.TotalAssessmentOutputTokens > 0;
            if (hasAssessor && pricing.Assessor != null)
            {
                byRole[AssessorRole] = (double)ModelPricingService.ComputeCost(
                    pricing.Assessor, run.TotalAssessmentInputTokens, run.TotalAssessmentOutputTokens);
            }

            bool hasVerifier = run.TotalClaimVerificationInputTokens > 0 || run.TotalClaimVerificationOutputTokens > 0;
            if (hasVerifier && pricing.ClaimVerifier != null)
            {
                byRole[ClaimVerifierRole] = (double)ModelPricingService.ComputeCost(
                    pricing.ClaimVerifier, run.TotalClaimVerificationInputTokens, run.TotalClaimVerificationOutputTokens);
            }

            costs.Add(new BenchmarkGroupRunCost { RunId = run.Id, CostByRole = byRole });
        }

        return costs;
    }
}
