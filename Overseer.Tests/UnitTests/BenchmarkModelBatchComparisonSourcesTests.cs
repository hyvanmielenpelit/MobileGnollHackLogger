namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Overseer.Tests.Helpers;
using Xunit;

/// <summary>
/// A model batch's comparison sources: the lists the batch dialog's Open in Model Comparison starts
/// from, as its client preset built them, and the baseline model's latest comparable result added
/// when the batch does not hold the baseline model, or the reason it was not.
/// </summary>
public class BenchmarkModelBatchComparisonSourcesTests
{
    private static readonly DateTime T0 = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private static ApplicationDbContext CreateDb()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static SystemAiConfigurationSnapshot BaselineSnapshot()
        => BenchmarkModelSnapshots.Model("OpenAI", "gpt-luna", displayName: "Luna", thinkingLevel: "high");

    private static SystemAiConfigurationSnapshot ModelSnapshot(int i)
        => BenchmarkModelSnapshots.Model("Anthropic", $"claude-{i}", displayName: $"Claude {i}", thinkingLevel: "high");

    private static async Task<long> AddSuiteAsync(ApplicationDbContext db)
    {
        var suite = new BenchmarkSuite { Name = "Suite A", Description = "Desc" };
        db.BenchmarkSuites.Add(suite);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return suite.Id;
    }

    private static async Task<BenchmarkRun> AddRunAsync(
        ApplicationDbContext db, long suiteId, SystemAiConfigurationSnapshot tested, DateTime startedAtUtc, long? seriesId = null)
    {
        var run = BenchmarkModelSnapshots.Attach(new BenchmarkRun
        {
            BenchmarkSuiteId = suiteId,
            SuiteName = "Suite A",
            TestedModelConfigurationId = 1,
            TestedModelSnapshot = tested,
            Status = BenchmarkRunStatus.Completed,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = startedAtUtc.AddMinutes(30),
            HarnessVersion = "h1",
            ScoringMethodVersion = 1,
            RunSeriesId = seriesId
        });
        db.BenchmarkRuns.Add(run);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return run;
    }

    private static async Task<BenchmarkRunSeries> AddSeriesAsync(ApplicationDbContext db, long? groupId)
    {
        var series = new BenchmarkRunSeries
        {
            SuiteName = "Suite A", StartRequestJson = "{}", RequestedRunCount = 2, CompletedRunCount = 2,
            Status = BenchmarkRunSeriesStatus.Completed, AutoCreatedGroupId = groupId
        };
        db.BenchmarkRunSeries.Add(series);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return series;
    }

    private static async Task<BenchmarkModelBatchRun> AddBatchAsync(
        ApplicationDbContext db, BenchmarkModelBatchTargetKind kind, long? suiteId, params BenchmarkModelBatchMember[] members)
    {
        for (int i = 0; i < members.Length; i++) members[i].OrderIndex = i;
        var batch = new BenchmarkModelBatchRun
        {
            TargetKind = kind,
            BenchmarkSuiteId = suiteId,
            BenchmarkBatteryId = kind == BenchmarkModelBatchTargetKind.Battery ? 3 : null,
            TargetName = "Target",
            StartRequestJson = "{}",
            Status = BenchmarkRunSeriesStatus.Completed,
            RequestedMemberCount = members.Length,
            Members = members.ToList()
        };
        db.BenchmarkModelBatchRuns.Add(batch);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return batch;
    }

    private static BenchmarkModelBatchMember Member(
        BenchmarkModelBatchMemberStatus status, long? runId = null, long? seriesId = null, long? batteryRunId = null)
        => new()
        {
            TestedModelConfigurationId = 1,
            Status = status,
            BenchmarkRunId = runId,
            BenchmarkRunSeriesId = seriesId,
            BenchmarkBatteryRunId = batteryRunId
        };

    private static async Task SetBaselineAsync(ApplicationDbContext db, SystemAiConfigurationSnapshot snapshot)
    {
        db.BenchmarkBaselineModelSettings.Add(new BenchmarkBaselineModelSetting { ModelSnapshot = snapshot, SetAtUtc = T0 });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// A suite batch: a single run, a series with its analysis group, a series without one, and two
    /// unfinished members. Returns the batch and the run ids the lists should hold.
    /// </summary>
    private static async Task<(BenchmarkModelBatchRun Batch, long SingleRunId, long[] UngroupedSeriesRunIds, long SuiteId)> SuiteBatchAsync(ApplicationDbContext db)
    {
        long suiteId = await AddSuiteAsync(db);
        var single = await AddRunAsync(db, suiteId, ModelSnapshot(1), T0.AddDays(10));

        var grouped = await AddSeriesAsync(db, groupId: 77);
        await AddRunAsync(db, suiteId, ModelSnapshot(2), T0.AddDays(11), grouped.Id);
        await AddRunAsync(db, suiteId, ModelSnapshot(2), T0.AddDays(11).AddHours(1), grouped.Id);

        var ungrouped = await AddSeriesAsync(db, groupId: null);
        var first = await AddRunAsync(db, suiteId, ModelSnapshot(3), T0.AddDays(12), ungrouped.Id);
        var second = await AddRunAsync(db, suiteId, ModelSnapshot(3), T0.AddDays(12).AddHours(1), ungrouped.Id);

        var failed = await AddRunAsync(db, suiteId, ModelSnapshot(4), T0.AddDays(13));

        var batch = await AddBatchAsync(db, BenchmarkModelBatchTargetKind.Suite, suiteId,
            Member(BenchmarkModelBatchMemberStatus.Completed, runId: single.Id),
            Member(BenchmarkModelBatchMemberStatus.CompletedWithErrors, seriesId: grouped.Id),
            Member(BenchmarkModelBatchMemberStatus.Completed, seriesId: ungrouped.Id),
            Member(BenchmarkModelBatchMemberStatus.Failed, runId: failed.Id),
            Member(BenchmarkModelBatchMemberStatus.Pending));

        return (batch, single.Id, new[] { first.Id, second.Id }, suiteId);
    }

    // --- The lists --------------------------------------------------------------------------------

    [Fact]
    public async Task ASuiteBatch_TakesRuns_TheGroupOfASeries_AndTheRunsOfASeriesWithoutOne_SkippingUnfinishedMembers()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateDb();
        var (batch, singleRunId, ungroupedRunIds, _) = await SuiteBatchAsync(db);

        var result = await new BenchmarkModelBatchComparisonSources(db).BuildAsync(batch.Id, ct);

        Assert.NotNull(result);
        Assert.Equal(new[] { singleRunId }.Concat(ungroupedRunIds).ToArray(), result!.RunIds.ToArray());
        Assert.Equal(new long[] { 77 }, result.GroupIds.ToArray());
        Assert.Empty(result.BatteryRunIds);
        Assert.Null(result.BaselineModelSource);
        Assert.Equal(BenchmarkModelBatchComparisonSources.NoBaselineModelReason, result.BaselineModelReason);
    }

    [Fact]
    public async Task ABatteryBatch_TakesTheFinishedMembersBatteryRuns_AtMostTwentyFour()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateDb();
        var members = Enumerable.Range(1, 26)
            .Select(i => Member(BenchmarkModelBatchMemberStatus.Completed, batteryRunId: 1000 + i))
            .Prepend(Member(BenchmarkModelBatchMemberStatus.Skipped, batteryRunId: 999))
            .ToArray();
        var batch = await AddBatchAsync(db, BenchmarkModelBatchTargetKind.Battery, null, members);

        var result = await new BenchmarkModelBatchComparisonSources(db).BuildAsync(batch.Id, ct);

        Assert.Equal(Enumerable.Range(1, BenchmarkModelBatchComparisonSources.MaxSources).Select(i => 1000L + i).ToArray(),
            result!.BatteryRunIds.ToArray());
        Assert.Empty(result.RunIds);
        Assert.Empty(result.GroupIds);
    }

    [Fact]
    public async Task ASuiteBatch_CapsRunsFirst_ThenGroups_AtTwentyFour()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateDb();
        long suiteId = await AddSuiteAsync(db);
        var members = new List<BenchmarkModelBatchMember>();
        for (int i = 0; i < 22; i++)
        {
            var run = await AddRunAsync(db, suiteId, ModelSnapshot(i), T0.AddHours(i));
            members.Add(Member(BenchmarkModelBatchMemberStatus.Completed, runId: run.Id));
        }
        for (int i = 0; i < 4; i++)
        {
            var series = await AddSeriesAsync(db, groupId: 500 + i);
            members.Add(Member(BenchmarkModelBatchMemberStatus.Completed, seriesId: series.Id));
        }
        var batch = await AddBatchAsync(db, BenchmarkModelBatchTargetKind.Suite, suiteId, members.ToArray());
        await SetBaselineAsync(db, BaselineSnapshot());

        var result = await new BenchmarkModelBatchComparisonSources(db).BuildAsync(batch.Id, ct);

        Assert.Equal(22, result!.RunIds.Count);
        Assert.Equal(new long[] { 500, 501 }, result.GroupIds.ToArray());
        Assert.Null(result.BaselineModelSource);
        Assert.Equal(BenchmarkModelBatchComparisonSources.ComparisonFullReason, result.BaselineModelReason);
    }

    [Fact]
    public async Task AnUnknownBatch_IsNull()
    {
        using var db = CreateDb();
        Assert.Null(await new BenchmarkModelBatchComparisonSources(db).BuildAsync(4242, TestContext.Current.CancellationToken));
    }

    // --- The baseline model -----------------------------------------------------------------------

    [Fact]
    public async Task TheBaselineModelAlreadyInTheBatch_AddsNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateDb();
        var (batch, _, _, _) = await SuiteBatchAsync(db);
        await SetBaselineAsync(db, ModelSnapshot(2));

        var result = await new BenchmarkModelBatchComparisonSources(db).BuildAsync(batch.Id, ct);

        Assert.Equal(3, result!.RunIds.Count);
        Assert.Null(result.BaselineModelSource);
        Assert.Equal(BenchmarkModelBatchComparisonSources.AlreadyInBatchReason, result.BaselineModelReason);
    }

    [Fact]
    public async Task TheBaselineModelsLatestComparableRun_IsAddedToTheRuns()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateDb();
        var (batch, singleRunId, ungroupedRunIds, suiteId) = await SuiteBatchAsync(db);
        await AddRunAsync(db, suiteId, BaselineSnapshot(), T0.AddDays(1));
        var latest = await AddRunAsync(db, suiteId, BaselineSnapshot(), T0.AddDays(2));
        await SetBaselineAsync(db, BaselineSnapshot());

        var result = await new BenchmarkModelBatchComparisonSources(db).BuildAsync(batch.Id, ct);

        Assert.Equal(new[] { singleRunId }.Concat(ungroupedRunIds).Append(latest.Id).ToArray(), result!.RunIds.ToArray());
        Assert.Equal(new long[] { 77 }, result.GroupIds.ToArray());
        Assert.NotNull(result.BaselineModelSource);
        Assert.Equal(latest.Id, result.BaselineModelSource!.RunId);
        Assert.Equal($"Luna, run #{latest.Id}", result.BaselineModelSource.Label);
        Assert.Equal(latest.StartedAtUtc, result.BaselineModelSource.StartedAtUtc);
        Assert.Null(result.BaselineModelReason);
    }

    [Fact]
    public async Task NoComparableBaselineResult_SaysSo()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateDb();
        var (batch, _, _, suiteId) = await SuiteBatchAsync(db);
        var other = await AddRunAsync(db, suiteId, BaselineSnapshot(), T0.AddDays(1));
        other.HarnessVersion = "h2";
        await db.SaveChangesAsync(ct);
        await SetBaselineAsync(db, BaselineSnapshot());

        var result = await new BenchmarkModelBatchComparisonSources(db).BuildAsync(batch.Id, ct);

        Assert.Equal(3, result!.RunIds.Count);
        Assert.DoesNotContain(other.Id, result.RunIds);
        Assert.Null(result.BaselineModelSource);
        Assert.Equal(BenchmarkModelBatchComparisonSources.NoneComparableReason, result.BaselineModelReason);
    }
}
