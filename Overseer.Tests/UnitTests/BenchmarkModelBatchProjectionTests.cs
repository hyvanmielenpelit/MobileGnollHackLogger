namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Overseer.Tests.Helpers;
using Xunit;

/// <summary>
/// The model batch projection: planned runs, the per-member basis (own runs, the target's mean,
/// mixed, none), and the whole-batch run-limit calculation at its boundaries (§ 3.5).
/// </summary>
public class BenchmarkModelBatchProjectionTests
{
    private static BenchmarkModelBatchProjectionInput Input(
        int models = 2,
        int runsPerModel = 1,
        bool battery = false,
        int suiteCount = 1,
        bool allowCapWait = false,
        int maxRunsPerDay = 120,
        int runsInLast24Hours = 0,
        int maxRunsPerHour = 30,
        int runsInLastHour = 0,
        int maxBatteryMembers = 120,
        double? meanWallMs = 600_000,
        BenchmarkSpendCheck? spend = null)
    {
        var suiteIds = Enumerable.Range(1, battery ? suiteCount : 1).Select(i => (long)(100 + i)).ToList();
        return new BenchmarkModelBatchProjectionInput
        {
            CandidateIds = Enumerable.Range(1, models).Select(i => (long)i).ToList(),
            SuiteIds = suiteIds,
            IsBattery = battery,
            RunsPerModel = runsPerModel,
            AllowCapWait = allowCapWait,
            SuiteBases = meanWallMs.HasValue
                ? suiteIds.ToDictionary(id => id, _ => new BenchmarkModelBatchRunBasis(5, meanWallMs, 4m, 1m))
                : new Dictionary<long, BenchmarkModelBatchRunBasis>(),
            Limits = new BenchmarkRunLimits(maxRunsPerHour, maxRunsPerDay, runsInLastHour, runsInLast24Hours,
                Math.Max(0, maxRunsPerDay - runsInLast24Hours)),
            MaxBatteryMembers = maxBatteryMembers,
            Spend = spend ?? BenchmarkSpendCheck.Allow
        };
    }

    // --- Planned runs and bases -------------------------------------------------------------------

    [Fact]
    public void PlannedRuns_AreModelsTimesR_OnASuite_AndModelsTimesKTimesR_OnABattery()
    {
        Assert.Equal(6, BenchmarkModelBatchProjection.Compute(Input(models: 3, runsPerModel: 2)).PlannedLaunches);

        var battery = BenchmarkModelBatchProjection.Compute(Input(models: 3, runsPerModel: 2, battery: true, suiteCount: 4));
        Assert.Equal(24, battery.PlannedLaunches);
        Assert.Equal(24, battery.Projection.PlannedRunCount);
        Assert.Equal(8, battery.Projection.Limits.MemberPlanRuns!.Value);
        Assert.All(battery.Projection.Members, m => Assert.Equal(8, m.PlannedRunCount));
    }

    [Fact]
    public void AMemberWithoutRunsOfItsOwn_IsProjectedFromTheTargetMean()
    {
        var result = BenchmarkModelBatchProjection.Compute(Input(models: 2, runsPerModel: 3));

        var member = result.Projection.Members[0];
        Assert.Equal(BenchmarkModelBatchProjectionBasis.TargetMean, member.Basis);
        Assert.Equal(12m, member.ProjectedCostUsd!.Value);
        Assert.Equal(1_800_000L, member.ProjectedWallMs!.Value);
        Assert.Equal(24m, result.Projection.ProjectedCostUsd!.Value);
        Assert.Equal(3_600_000L, result.Projection.ProjectedWallMs!.Value);
    }

    [Fact]
    public void AMemberWithItsOwnRuns_ReplacesTheCandidateShare_AndUsesItsOwnWallTime()
    {
        var input = Input(models: 2) with
        {
            ModelBases = new Dictionary<(long ConfigurationId, long SuiteId), BenchmarkModelBatchRunBasis>
            {
                [(1, 101)] = new BenchmarkModelBatchRunBasis(2, 120_000, 10m, 7m)
            }
        };

        var result = BenchmarkModelBatchProjection.Compute(input);
        var own = result.Projection.Members.Single(m => m.ModelConfigurationId == 1);
        var other = result.Projection.Members.Single(m => m.ModelConfigurationId == 2);

        // Graders 4 − 1 = 3, plus its own candidate share 7.
        Assert.Equal(BenchmarkModelBatchProjectionBasis.OwnRuns, own.Basis);
        Assert.Equal(10m, own.ProjectedCostUsd!.Value);
        Assert.Equal(120_000L, own.ProjectedWallMs!.Value);
        Assert.Equal(BenchmarkModelBatchProjectionBasis.TargetMean, other.Basis);
    }

    [Fact]
    public void ABatteryMemberWithOwnRunsOnSomeSuites_IsMixed_AndOneWithoutAnyBasis_IsNone()
    {
        var mixed = Input(models: 1, battery: true, suiteCount: 2) with
        {
            ModelBases = new Dictionary<(long ConfigurationId, long SuiteId), BenchmarkModelBatchRunBasis>
            {
                [(1, 101)] = new BenchmarkModelBatchRunBasis(1, 60_000, 3m, 2m)
            }
        };
        Assert.Equal(BenchmarkModelBatchProjectionBasis.Mixed, BenchmarkModelBatchProjection.Compute(mixed).Projection.Members[0].Basis);

        var none = BenchmarkModelBatchProjection.Compute(Input(models: 2, meanWallMs: null));
        Assert.All(none.Projection.Members, m => Assert.Equal(BenchmarkModelBatchProjectionBasis.None, m.Basis));
        Assert.Null(none.Projection.ProjectedCostUsd);
        Assert.Null(none.Projection.ProjectedWallMs);
        Assert.Null(none.Projection.Limits.ProjectedRunsPerHour);
    }

    // --- The run limits at their boundaries -------------------------------------------------------

    [Fact]
    public void LaunchesEqualToTheHeadroom_AreNeitherAWarningNorABlocker()
    {
        var result = BenchmarkModelBatchProjection.Compute(Input(models: 2, runsPerModel: 5, runsInLast24Hours: 110));

        Assert.Equal(10, result.PlannedLaunches);
        Assert.Equal(10, result.Projection.Limits.RemainingDailyHeadroom);
        Assert.False(result.ExceedsDailyHeadroom);
        Assert.False(result.ExceedsDailyCap);
    }

    [Fact]
    public void LaunchesEqualToTheCap_ExceedTheHeadroom_ButNotTheCap()
    {
        var result = BenchmarkModelBatchProjection.Compute(Input(models: 2, runsPerModel: 60, runsInLast24Hours: 1));

        Assert.Equal(120, result.PlannedLaunches);
        Assert.True(result.ExceedsDailyHeadroom);
        Assert.False(result.ExceedsDailyCap);
        Assert.Equal(1, result.Projection.Limits.DaySpan!.Value);
        Assert.Null(result.Projection.Limits.MinimumWallMs);
    }

    [Fact]
    public void OneLaunchAboveTheCap_ExceedsIt_AndSpansTwoWindows()
    {
        var input = Input(models: 1, runsPerModel: 121, maxRunsPerDay: 120);
        var withoutWait = BenchmarkModelBatchProjection.Compute(input with { RunsPerModel = 121 });

        // A series of 121 also exceeds what one series may plan (the daily cap).
        Assert.True(withoutWait.ExceedsDailyCap);
        Assert.False(withoutWait.ExceedsDailyHeadroom);
        Assert.Equal(2, withoutWait.Projection.Limits.DaySpan!.Value);
        Assert.Equal((long)TimeSpan.FromHours(24).TotalMilliseconds, withoutWait.Projection.Limits.MinimumWallMs!.Value);

        var spread = BenchmarkModelBatchProjection.Compute(Input(models: 11, runsPerModel: 11, allowCapWait: true));
        Assert.Equal(121, spread.PlannedLaunches);
        Assert.True(spread.ExceedsDailyCap);
        Assert.False(spread.MemberPlanExceeds);
        Assert.Equal(2, spread.Projection.Limits.DaySpan!.Value);
    }

    [Fact]
    public void ABatteryMemberPlanOneAboveMaxMembers_Exceeds()
    {
        var at = BenchmarkModelBatchProjection.Compute(Input(models: 1, battery: true, suiteCount: 4, runsPerModel: 5, maxBatteryMembers: 20, maxRunsPerDay: 1000));
        var over = BenchmarkModelBatchProjection.Compute(Input(models: 1, battery: true, suiteCount: 3, runsPerModel: 7, maxBatteryMembers: 20, maxRunsPerDay: 1000));

        Assert.False(at.MemberPlanExceeds);
        Assert.True(over.MemberPlanExceeds);
        Assert.Equal(21, over.Projection.Limits.MemberPlanRuns!.Value);
        Assert.Equal(20, over.MemberPlanLimit);
    }

    [Fact]
    public void AOneMinuteMeanRun_OutpacesTheHourlyCap()
    {
        var fast = BenchmarkModelBatchProjection.Compute(Input(models: 4, runsPerModel: 10, meanWallMs: 60_000, runsInLastHour: 5));

        Assert.Equal(60d, fast.Projection.Limits.ProjectedRunsPerHour!.Value, 6);
        Assert.Equal(25, fast.HourlyHeadroom);
        Assert.True(fast.HourlyCapRisk);

        var slow = BenchmarkModelBatchProjection.Compute(Input(models: 4, runsPerModel: 10, meanWallMs: 180_000));
        Assert.Equal(20d, slow.Projection.Limits.ProjectedRunsPerHour!.Value, 6);
        Assert.False(slow.HourlyCapRisk);
    }

    [Fact]
    public void ASpendDenial_RefusesTheStart_UnlessItIsACapDenialAndTheBatchWaits()
    {
        var other = BenchmarkSpendCheck.Deny(BenchmarkSpendDenialKind.Other, "Suspended.");
        var cap = BenchmarkSpendCheck.Deny(BenchmarkSpendDenialKind.HourlyCap, "Hourly cap reached.");

        Assert.True(BenchmarkModelBatchProjection.Compute(Input(spend: other, allowCapWait: true)).SpendRefusesStart);
        Assert.True(BenchmarkModelBatchProjection.Compute(Input(spend: cap)).SpendRefusesStart);

        var waiting = BenchmarkModelBatchProjection.Compute(Input(spend: cap, allowCapWait: true));
        Assert.False(waiting.SpendRefusesStart);
        Assert.False(waiting.Projection.Limits.SpendAllowedNow);
        Assert.True(waiting.Projection.Limits.SpendDenialIsCap);
        Assert.Equal("Hourly cap reached.", waiting.Projection.Limits.SpendDenialReason);
    }

    // --- Loading the bases ------------------------------------------------------------------------

    [Fact]
    public async Task LoadBases_TakesTheLastFiveCompletedRunsPerSuite_AndEachModelsOwn()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var start = DateTime.UtcNow.AddDays(-2);
        for (int i = 0; i < 7; i++)
        {
            db.BenchmarkRuns.Add(BenchmarkModelSnapshots.Attach(new BenchmarkRun
            {
                BenchmarkSuiteId = 101,
                BenchmarkSuiteIdUsed = 101,
                SuiteName = "Suite",
                TestedModelConfigurationId = i < 2 ? 1 : 9,
                Status = BenchmarkRunStatus.Completed,
                StartedAtUtc = start.AddHours(i),
                TotalDurationMs = (i + 1) * 60_000
            }));
        }

        // A failed run is no basis.
        db.BenchmarkRuns.Add(BenchmarkModelSnapshots.Attach(new BenchmarkRun
        {
            BenchmarkSuiteId = 101,
            SuiteName = "Suite",
            TestedModelConfigurationId = 2,
            Status = BenchmarkRunStatus.Failed,
            StartedAtUtc = start.AddHours(10),
            TotalDurationMs = 999_999
        }));
        await db.SaveChangesAsync(ct);

        var (suites, models) = await BenchmarkModelBatchProjection.LoadBasesAsync(
            db, new BenchmarkRunCostEstimator(), new long[] { 101 }, new long[] { 1, 2 }, ct);

        // The newest five completed: durations 3..7 minutes, mean 5 minutes.
        Assert.Equal(5, suites[101].RunCount);
        Assert.Equal(300_000d, suites[101].MeanWallMs!.Value, 6);
        Assert.True(models.ContainsKey((1, 101)));
        Assert.Equal(2, models[(1, 101)].RunCount);
        Assert.Equal(90_000d, models[(1, 101)].MeanWallMs!.Value, 6);
        Assert.False(models.ContainsKey((2, 101)));
    }
}
