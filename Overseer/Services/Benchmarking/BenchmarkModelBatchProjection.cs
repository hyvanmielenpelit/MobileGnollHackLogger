namespace Overseer.Services.Benchmarking;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Models;

/// <summary>The mean of some recent completed runs: what one projection line is computed from.</summary>
public sealed record BenchmarkModelBatchRunBasis(
    int RunCount,
    double? MeanWallMs,
    decimal? MeanTotalCostUsd,
    decimal? MeanCandidateCostUsd);

/// <summary>Everything <see cref="BenchmarkModelBatchProjection.Compute"/> reads. Pure data.</summary>
public sealed record BenchmarkModelBatchProjectionInput
{
    /// <summary>The models under test, in request order.</summary>
    public IReadOnlyList<long> CandidateIds { get; init; } = Array.Empty<long>();

    /// <summary>The target's suites: one for a suite target, K for a battery.</summary>
    public IReadOnlyList<long> SuiteIds { get; init; } = Array.Empty<long>();

    public bool IsBattery { get; init; }

    /// <summary>R.</summary>
    public int RunsPerModel { get; init; } = 1;

    public bool AllowCapWait { get; init; }

    /// <summary>Per suite, the mean of its last completed runs, whichever models made them.</summary>
    public IReadOnlyDictionary<long, BenchmarkModelBatchRunBasis> SuiteBases { get; init; }
        = new Dictionary<long, BenchmarkModelBatchRunBasis>();

    /// <summary>Per (configuration, suite), the mean of that model's own last completed runs on the suite.</summary>
    public IReadOnlyDictionary<(long ConfigurationId, long SuiteId), BenchmarkModelBatchRunBasis> ModelBases { get; init; }
        = new Dictionary<(long ConfigurationId, long SuiteId), BenchmarkModelBatchRunBasis>();

    public BenchmarkRunLimits Limits { get; init; } = new(0, 0, 0, 0, 0);

    /// <summary><c>Benchmark:Battery:MaxMembers</c>.</summary>
    public int MaxBatteryMembers { get; init; }

    /// <summary>The spend guard's verdict now.</summary>
    public BenchmarkSpendCheck Spend { get; init; } = BenchmarkSpendCheck.Allow;
}

/// <summary>The projection and the run-limit verdicts the guardrails read (§ 3.5).</summary>
public sealed record BenchmarkModelBatchProjectionResult
{
    public BenchmarkModelBatchProjectionDto Projection { get; init; } = new();

    /// <summary>L: models × R, or models × K × R.</summary>
    public int PlannedLaunches { get; init; }

    /// <summary>L above the daily cap.</summary>
    public bool ExceedsDailyCap { get; init; }

    /// <summary>L above the remaining 24-hour headroom, but not above the daily cap (MB-W11).</summary>
    public bool ExceedsDailyHeadroom { get; init; }

    /// <summary>One member plans more launches than its own launcher admits (MB-B08 member-plan variant).</summary>
    public bool MemberPlanExceeds { get; init; }

    /// <summary>The limit <see cref="MemberPlanExceeds"/> is measured against.</summary>
    public int MemberPlanLimit { get; init; }

    /// <summary>The projected launch rate is above the hourly headroom and the batch plans more than that headroom (MB-W12).</summary>
    public bool HourlyCapRisk { get; init; }

    /// <summary><c>MaxRunsPerHour − RunsInLastHour</c>, floored at zero.</summary>
    public int HourlyHeadroom { get; init; }

    /// <summary>The spend guard refuses a launch now and a cap wait would not outlast it (MB-B08 spend variant).</summary>
    public bool SpendRefusesStart { get; init; }
}

/// <summary>
/// Planned runs, projected cost and wall time per member and in total, and the whole-batch run-limit
/// calculation the battery launcher makes for one battery run, made over every member.
///
/// <para>The basis is the battery launcher's: the mean of a suite's last
/// <see cref="BasisRunCount"/> completed runs. A model with completed runs of its own on that suite
/// is projected from its own mean wall time, and its own candidate cost replaces the candidate share
/// of the suite mean; each member line names the basis it used.</para>
/// </summary>
public static class BenchmarkModelBatchProjection
{
    /// <summary>The recent completed runs a mean is taken over.</summary>
    public const int BasisRunCount = 5;

    private static readonly BenchmarkRunStatus[] CompletedStatuses =
    {
        BenchmarkRunStatus.Completed,
        BenchmarkRunStatus.CompletedWithLimits,
        BenchmarkRunStatus.CompletedWithErrors
    };

    public static BenchmarkModelBatchProjectionResult Compute(BenchmarkModelBatchProjectionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        int runsPerModel = Math.Max(0, input.RunsPerModel);
        int suiteCount = input.IsBattery ? input.SuiteIds.Count : 1;
        int memberPlan = suiteCount * runsPerModel;
        int launches = input.CandidateIds.Count * memberPlan;

        var members = new List<BenchmarkModelBatchMemberProjectionDto>();
        foreach (long candidateId in input.CandidateIds)
        {
            members.Add(ProjectMember(input, candidateId, runsPerModel, memberPlan));
        }

        decimal? totalCost = members.Count > 0 && members.All(m => m.ProjectedCostUsd.HasValue)
            ? members.Sum(m => m.ProjectedCostUsd!.Value)
            : null;
        long? totalWall = members.Count > 0 && members.All(m => m.ProjectedWallMs.HasValue)
            ? members.Sum(m => m.ProjectedWallMs!.Value)
            : null;

        var limits = input.Limits;
        int? daySpan = limits.MaxRunsPerDay > 0 && launches > 0
            ? (int)Math.Ceiling(launches / (double)limits.MaxRunsPerDay)
            : null;
        long? minimumWallMs = daySpan is int span && span > 1
            ? (long)TimeSpan.FromHours(24 * (span - 1)).TotalMilliseconds
            : null;

        var suiteMeans = input.SuiteIds
            .Select(id => input.SuiteBases.TryGetValue(id, out var b) ? b.MeanWallMs : null)
            .Where(ms => ms.HasValue && ms.Value > 0)
            .Select(ms => ms!.Value)
            .ToList();
        double? runsPerHour = suiteMeans.Count == 0
            ? null
            : TimeSpan.FromHours(1).TotalMilliseconds / suiteMeans.Min();

        int hourlyHeadroom = Math.Max(0, limits.MaxRunsPerHour - limits.RunsInLastHour);
        int memberPlanLimit = input.IsBattery ? input.MaxBatteryMembers : limits.MaxRunsPerDay;

        bool exceedsCap = limits.MaxRunsPerDay > 0 && launches > limits.MaxRunsPerDay;
        bool exceedsHeadroom = !exceedsCap && launches > limits.RemainingDailyHeadroom;
        bool memberPlanExceeds = memberPlanLimit > 0 && memberPlan > memberPlanLimit;
        bool hourlyRisk = runsPerHour.HasValue && runsPerHour.Value > hourlyHeadroom && launches > hourlyHeadroom;
        bool spendRefuses = !input.Spend.Allowed && (!input.AllowCapWait || !input.Spend.IsCapDenial);

        return new BenchmarkModelBatchProjectionResult
        {
            Projection = new BenchmarkModelBatchProjectionDto
            {
                PlannedRunCount = launches,
                ProjectedCostUsd = totalCost,
                ProjectedWallMs = totalWall,
                Members = members,
                Limits = new BenchmarkModelBatchLimitsDto
                {
                    MaxRunsPerDay = limits.MaxRunsPerDay,
                    MaxRunsPerHour = limits.MaxRunsPerHour,
                    RunsInLast24Hours = limits.RunsInLast24Hours,
                    RunsInLastHour = limits.RunsInLastHour,
                    RemainingDailyHeadroom = limits.RemainingDailyHeadroom,
                    DaySpan = daySpan,
                    MinimumWallMs = minimumWallMs,
                    ProjectedRunsPerHour = runsPerHour,
                    MemberPlanRuns = memberPlan,
                    MaxBatteryMembers = input.MaxBatteryMembers,
                    SpendAllowedNow = input.Spend.Allowed,
                    SpendDenialReason = input.Spend.Allowed ? null : input.Spend.DenialReason,
                    SpendDenialIsCap = !input.Spend.Allowed && input.Spend.IsCapDenial
                }
            },
            PlannedLaunches = launches,
            ExceedsDailyCap = exceedsCap,
            ExceedsDailyHeadroom = exceedsHeadroom,
            MemberPlanExceeds = memberPlanExceeds,
            MemberPlanLimit = memberPlanLimit,
            HourlyCapRisk = hourlyRisk,
            HourlyHeadroom = hourlyHeadroom,
            SpendRefusesStart = spendRefuses
        };
    }

    private static BenchmarkModelBatchMemberProjectionDto ProjectMember(
        BenchmarkModelBatchProjectionInput input,
        long candidateId,
        int runsPerModel,
        int memberPlan)
    {
        var suiteIds = input.IsBattery ? input.SuiteIds : input.SuiteIds.Take(1).ToList();
        decimal? cost = 0m;
        double? wall = 0d;
        int own = 0;
        bool missing = suiteIds.Count == 0;

        foreach (long suiteId in suiteIds)
        {
            input.SuiteBases.TryGetValue(suiteId, out var suite);
            input.ModelBases.TryGetValue((candidateId, suiteId), out var model);

            if (model != null && model.RunCount > 0)
            {
                own++;
                wall = Add(wall, model.MeanWallMs ?? suite?.MeanWallMs);
                cost = Add(cost, OwnCost(suite, model));
            }
            else if (suite != null && suite.RunCount > 0)
            {
                wall = Add(wall, suite.MeanWallMs);
                cost = Add(cost, suite.MeanTotalCostUsd);
            }
            else
            {
                missing = true;
                wall = null;
                cost = null;
            }
        }

        string basis = missing
            ? BenchmarkModelBatchProjectionBasis.None
            : own == suiteIds.Count
                ? BenchmarkModelBatchProjectionBasis.OwnRuns
                : own > 0
                    ? BenchmarkModelBatchProjectionBasis.Mixed
                    : BenchmarkModelBatchProjectionBasis.TargetMean;

        return new BenchmarkModelBatchMemberProjectionDto
        {
            ModelConfigurationId = candidateId,
            PlannedRunCount = memberPlan,
            ProjectedCostUsd = cost * runsPerModel,
            ProjectedWallMs = wall.HasValue ? (long)Math.Round(wall.Value * runsPerModel) : null,
            Basis = basis
        };
    }

    /// <summary>
    /// The suite mean with its candidate share replaced by the model's own: the graders cost what
    /// they cost on this suite, the candidate what it cost on its own runs.
    /// </summary>
    private static decimal? OwnCost(BenchmarkModelBatchRunBasis? suite, BenchmarkModelBatchRunBasis model)
    {
        if (suite?.MeanTotalCostUsd is decimal total && suite.MeanCandidateCostUsd is decimal candidate
            && model.MeanCandidateCostUsd is decimal own)
        {
            return Math.Max(0m, total - candidate) + own;
        }

        return model.MeanTotalCostUsd;
    }

    private static double? Add(double? sum, double? value) => sum.HasValue && value.HasValue ? sum + value : null;

    private static decimal? Add(decimal? sum, decimal? value) => sum.HasValue && value.HasValue ? sum + value : null;

    // ---------------------------------------------------------------------------------------
    // Loading the bases
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The per-suite and per-(model, suite) means of the last <see cref="BasisRunCount"/> completed
    /// runs, newest first. Wall time is the run's total duration, else its summed answer time; cost
    /// is the run's estimate on its own pricing snapshot, null when it cannot be priced.
    /// </summary>
    public static async Task<(Dictionary<long, BenchmarkModelBatchRunBasis> Suites,
                               Dictionary<(long ConfigurationId, long SuiteId), BenchmarkModelBatchRunBasis> Models)>
        LoadBasesAsync(
            ApplicationDbContext db,
            BenchmarkRunCostEstimator estimator,
            IReadOnlyCollection<long> suiteIds,
            IReadOnlyCollection<long> candidateIds,
            CancellationToken ct = default)
    {
        var suites = new Dictionary<long, BenchmarkModelBatchRunBasis>();
        var models = new Dictionary<(long, long), BenchmarkModelBatchRunBasis>();

        foreach (long suiteId in suiteIds.Distinct())
        {
            var recent = await db.BenchmarkRuns
                .AsNoTracking()
                .Where(r => (r.BenchmarkSuiteIdUsed ?? r.BenchmarkSuiteId) == suiteId && CompletedStatuses.Contains(r.Status))
                .OrderByDescending(r => r.StartedAtUtc)
                .ThenByDescending(r => r.Id)
                .Take(BasisRunCount)
                .ToListAsync(ct);
            suites[suiteId] = await BasisOfAsync(recent, estimator);

            foreach (long candidateId in candidateIds.Distinct())
            {
                var own = await db.BenchmarkRuns
                    .AsNoTracking()
                    .Where(r => (r.BenchmarkSuiteIdUsed ?? r.BenchmarkSuiteId) == suiteId
                                && r.TestedModelConfigurationId == candidateId
                                && CompletedStatuses.Contains(r.Status))
                    .OrderByDescending(r => r.StartedAtUtc)
                    .ThenByDescending(r => r.Id)
                    .Take(BasisRunCount)
                    .ToListAsync(ct);
                if (own.Count > 0)
                {
                    models[(candidateId, suiteId)] = await BasisOfAsync(own, estimator);
                }
            }
        }

        return (suites, models);
    }

    private static async Task<BenchmarkModelBatchRunBasis> BasisOfAsync(IReadOnlyList<BenchmarkRun> runs, BenchmarkRunCostEstimator estimator)
    {
        if (runs.Count == 0) return new BenchmarkModelBatchRunBasis(0, null, null, null);

        var walls = runs
            .Select(r => r.TotalDurationMs > 0 ? r.TotalDurationMs : r.TotalAnswerDurationMs)
            .Where(ms => ms > 0)
            .Select(ms => (double)ms)
            .ToList();

        var totals = new List<decimal>();
        var candidates = new List<decimal>();
        foreach (var run in runs)
        {
            var estimate = await estimator.EstimateAsync(run, run, null);
            if (estimate.Total is decimal total) totals.Add(total);
            if (estimate.Candidate is decimal candidate) candidates.Add(candidate);
        }

        return new BenchmarkModelBatchRunBasis(
            runs.Count,
            walls.Count > 0 ? walls.Average() : null,
            totals.Count > 0 ? totals.Average() : null,
            candidates.Count > 0 ? candidates.Average() : null);
    }
}
