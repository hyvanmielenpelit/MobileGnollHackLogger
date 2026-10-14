namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Models;
using Overseer.Services.Benchmarking;
using Overseer.Services.ChatConsistency;
using Overseer.Tests.Helpers;
using Xunit;

/// <summary>
/// The GnollBench baseline model: an append-only history of settings, set from a configuration with the
/// Benchmark role, matched to comparison entries by the model-axis key of its snapshot.
/// </summary>
public class BenchmarkBaselineModelServiceTests
{
    private static ApplicationDbContext CreateDb()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static SystemAiApiConfiguration Configuration(
        long id, string displayName = "GPT Luna (high)", string thinkingLevel = "high", int modelRole = 7)
        => new()
        {
            Id = id,
            DisplayName = displayName,
            Provider = "OpenAI",
            ModelId = "gpt-luna",
            ThinkingLevel = thinkingLevel,
            IsEnabled = true,
            ModelRole = modelRole,
        };

    [Fact]
    public async Task NothingSet_GetReturnsNull()
    {
        using var db = CreateDb();
        var service = new BenchmarkBaselineModelService(db);

        Assert.Null(await service.GetAsync(TestContext.Current.CancellationToken));
        Assert.Null(await service.AxisKeyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Set_CapturesTheConfigurationsSnapshotAndReturnsIt()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateDb();
        db.SystemAiApiConfigurations.Add(Configuration(5));
        await db.SaveChangesAsync(ct);
        var service = new BenchmarkBaselineModelService(db);

        var (result, error) = await service.SetAsync(5, "admin-1", ct);

        Assert.Null(error);
        Assert.NotNull(result);
        Assert.Equal("GPT Luna (high)", result!.DisplayName);
        Assert.Equal("OpenAI", result.Provider);
        Assert.Equal("gpt-luna", result.ModelId);
        Assert.Equal("high", result.ThinkingLevel);
        Assert.Equal(5, result.SourceConfigurationId);

        var row = Assert.Single(db.BenchmarkBaselineModelSettings);
        Assert.Equal(result.SnapshotId, row.ModelSnapshotId);
        Assert.Equal("admin-1", row.SetByUserId);
        Assert.Equal(result.SnapshotId, (await service.GetAsync(ct))!.SnapshotId);
    }

    [Fact]
    public async Task SettingTheSameSettingsTwice_ReusesTheSnapshotAndKeepsBothHistoryRows()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateDb();
        db.SystemAiApiConfigurations.Add(Configuration(5));
        await db.SaveChangesAsync(ct);
        var service = new BenchmarkBaselineModelService(db);

        var (first, _) = await service.SetAsync(5, null, ct);
        var (second, _) = await service.SetAsync(5, null, ct);

        Assert.Equal(first!.SnapshotId, second!.SnapshotId);
        Assert.Single(db.SystemAiConfigurationSnapshots);
        Assert.Equal(2, db.BenchmarkBaselineModelSettings.Count());
    }

    [Fact]
    public async Task Clear_AppendsAClearedRowAndKeepsTheHistory()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateDb();
        db.SystemAiApiConfigurations.Add(Configuration(5));
        await db.SaveChangesAsync(ct);
        var service = new BenchmarkBaselineModelService(db);
        await service.SetAsync(5, null, ct);

        var (result, error) = await service.SetAsync(null, null, ct);

        Assert.Null(error);
        Assert.Null(result);
        Assert.Null(await service.GetAsync(ct));
        var rows = db.BenchmarkBaselineModelSettings.OrderBy(s => s.Id).ToList();
        Assert.Equal(2, rows.Count);
        Assert.NotNull(rows[0].ModelSnapshotId);
        Assert.Null(rows[1].ModelSnapshotId);
        Assert.Null(rows[1].SourceConfigurationId);
    }

    [Fact]
    public async Task Set_RefusesAMissingConfigurationAndOneWithoutTheBenchmarkRole()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateDb();
        db.SystemAiApiConfigurations.Add(Configuration(5, displayName: "Chat Only", modelRole: 3));
        await db.SaveChangesAsync(ct);
        var service = new BenchmarkBaselineModelService(db);

        var (missing, missingError) = await service.SetAsync(99, null, ct);
        var (chatOnly, roleError) = await service.SetAsync(5, null, ct);

        Assert.Null(missing);
        Assert.Contains("99", missingError);
        Assert.Null(chatOnly);
        Assert.Contains("Benchmark role", roleError);
        Assert.Empty(db.BenchmarkBaselineModelSettings);
    }

    [Fact]
    public void IsBaselineModel_MatchesARenameAndNotAnotherThinkingLevel()
    {
        string baselineKey = ChatConsistencyComparability.ModelAxisKey(
            SystemAiConfigurationSnapshotStore.FromConfiguration(Configuration(5)));

        Assert.True(BenchmarkBaselineModelService.IsBaselineModel(Configuration(6, displayName: "Renamed"), baselineKey));
        Assert.False(BenchmarkBaselineModelService.IsBaselineModel(Configuration(7, thinkingLevel: "medium"), baselineKey));
        Assert.False(BenchmarkBaselineModelService.IsBaselineModel(Configuration(5), null));
    }

    private static BenchmarkModelComparisonEntryDto Entry(string key, bool excluded = false, int day = 1)
        => new() { Key = key, Excluded = excluded, LastRunStartedAtUtc = new DateTime(2026, 10, day, 0, 0, 0, DateTimeKind.Utc) };

    [Fact]
    public void Resolve_ReportsEveryState()
    {
        const string baseline = "baseline-key";

        Assert.Equal(((string?)null, BenchmarkBaselineModelStates.NotSet),
            BenchmarkBaselineModelService.Resolve(null, new[] { (Entry("run:1"), baseline) }));
        Assert.Equal(((string?)null, BenchmarkBaselineModelStates.NotInComparison),
            BenchmarkBaselineModelService.Resolve(baseline, new[] { (Entry("run:1"), "other") }));
        Assert.Equal(((string?)"run:1", BenchmarkBaselineModelStates.Excluded),
            BenchmarkBaselineModelService.Resolve(baseline, new[] { (Entry("run:1", excluded: true), baseline) }));
        Assert.Equal(((string?)"run:2", BenchmarkBaselineModelStates.Included),
            BenchmarkBaselineModelService.Resolve(baseline, new[] { (Entry("run:1", excluded: true, day: 9), baseline), (Entry("run:2"), baseline) }));
    }

    [Fact]
    public void Resolve_PrefersTheNewestThenTheEntryKey()
    {
        const string baseline = "baseline-key";

        Assert.Equal("group:4", BenchmarkBaselineModelService.Resolve(baseline, new[]
        {
            (Entry("run:1", day: 1), baseline),
            (Entry("group:4", day: 3), baseline),
            (Entry("run:9", day: 2), baseline),
        }).EntryKey);
        Assert.Equal("run:1", BenchmarkBaselineModelService.Resolve(baseline, new[]
        {
            (Entry("run:2", day: 3), baseline),
            (Entry("run:1", day: 3), baseline),
        }).EntryKey);
    }

    [Fact]
    public void Apply_MatchesEntriesByTheirNewestRunsModelAxisKey()
    {
        var snapshot = BenchmarkModelSnapshots.Model(provider: "OpenAI", modelId: "gpt-luna", displayName: "Luna", thinkingLevel: "high");
        snapshot.Id = 40;
        var setting = new BenchmarkBaselineModelSetting { Id = 1, ModelSnapshotId = 40, ModelSnapshot = snapshot, SetAtUtc = DateTime.UtcNow };

        var result = new BenchmarkModelComparisonDto { Entries = { Entry("run:1"), Entry("run:2") } };
        var runs = new Dictionary<string, BenchmarkRun?>
        {
            ["run:1"] = new BenchmarkRun { Id = 1, TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: "OpenAI", modelId: "gpt-luna", displayName: "Luna renamed", thinkingLevel: "high") },
            ["run:2"] = new BenchmarkRun { Id = 2, TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: "OpenAI", modelId: "gpt-luna", thinkingLevel: "medium") },
        };

        BenchmarkBaselineModelService.Apply(result, setting, runs);

        Assert.Equal("run:1", result.BaselineModelEntryKey);
        Assert.Equal(BenchmarkBaselineModelStates.Included, result.BaselineModelState);
        Assert.Equal("Luna", result.BaselineModel!.DisplayName);
        Assert.Equal(40, result.BaselineModel.SnapshotId);
    }

    [Fact]
    public async Task Controller_GetIs204UntilSet_PutRefusesAMissingConfiguration()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateDb();
        db.SystemAiApiConfigurations.Add(Configuration(5));
        await db.SaveChangesAsync(ct);
        var controller = new AdminBenchmarkBaselineModelController(new BenchmarkBaselineModelService(db));

        Assert.IsType<NoContentResult>(await controller.Get(ct));
        Assert.IsType<BadRequestObjectResult>(await controller.Put(new BenchmarkBaselineModelSetRequest { ConfigurationId = 99 }, ct));

        var set = Assert.IsType<OkObjectResult>(await controller.Put(new BenchmarkBaselineModelSetRequest { ConfigurationId = 5 }, ct));
        Assert.Equal("gpt-luna", Assert.IsType<BenchmarkBaselineModelDto>(set.Value).ModelId);
        Assert.IsType<OkObjectResult>(await controller.Get(ct));

        Assert.IsType<NoContentResult>(await controller.Put(new BenchmarkBaselineModelSetRequest { ConfigurationId = null }, ct));
        Assert.IsType<NoContentResult>(await controller.Get(ct));
    }
}
