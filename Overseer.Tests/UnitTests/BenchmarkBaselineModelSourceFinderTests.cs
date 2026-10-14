namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Overseer.Services.ChatConsistency;
using Overseer.Tests.Helpers;
using Xunit;

/// <summary>
/// The baseline model's latest result comparable with a model batch: on a suite, the newest finished
/// run with the baseline's model-axis key and the members' must-match signature, within ten tries; on
/// a battery, the newest ranked battery run of the members' definition and class whose newest member
/// run is the baseline model.
/// </summary>
public class BenchmarkBaselineModelSourceFinderTests
{
    private static readonly DateTime T0 = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private static ApplicationDbContext CreateDb()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static SystemAiConfigurationSnapshot BaselineSnapshot()
        => BenchmarkModelSnapshots.Model("OpenAI", "gpt-luna", displayName: "Luna", thinkingLevel: "high");

    private static SystemAiConfigurationSnapshot OtherSnapshot()
        => BenchmarkModelSnapshots.Model("Anthropic", "claude-opus-5", displayName: "Opus", thinkingLevel: "high");

    private static string BaselineKey => ChatConsistencyComparability.ModelAxisKey(BaselineSnapshot());

    private static async Task<long> AddSuiteAsync(ApplicationDbContext db)
    {
        var suite = new BenchmarkSuite { Name = "Suite A", Description = "Desc" };
        db.BenchmarkSuites.Add(suite);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return suite.Id;
    }

    private static async Task<BenchmarkRun> AddRunAsync(
        ApplicationDbContext db,
        long suiteId,
        SystemAiConfigurationSnapshot tested,
        DateTime startedAtUtc,
        string harness = "h1",
        BenchmarkRunStatus status = BenchmarkRunStatus.Completed)
    {
        var run = BenchmarkModelSnapshots.Attach(new BenchmarkRun
        {
            BenchmarkSuiteId = suiteId,
            SuiteName = "Suite A",
            TestedModelConfigurationId = 1,
            TestedModelSnapshot = tested,
            Status = status,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = startedAtUtc.AddMinutes(30),
            HarnessVersion = harness,
            ScoringMethodVersion = 1
        });
        db.BenchmarkRuns.Add(run);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return run;
    }

    /// <summary>One member per run, each read untracked as the batch reads it.</summary>
    private static async Task<IReadOnlyList<IReadOnlyList<BenchmarkRun>>> MembersAsync(ApplicationDbContext db, params BenchmarkRun[] runs)
    {
        var members = new List<IReadOnlyList<BenchmarkRun>>();
        foreach (var run in runs)
        {
            members.Add(await db.BenchmarkRuns.AsNoTracking().Where(r => r.Id == run.Id).ToListAsync(TestContext.Current.CancellationToken));
        }
        return members;
    }

    // --- Suite ------------------------------------------------------------------------------------

    [Fact]
    public async Task Suite_FindsTheNewestComparableBaselineRun_SkippingANonComparableOneAndOtherModels()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateDb();
        long suiteId = await AddSuiteAsync(db);
        var member = await AddRunAsync(db, suiteId, OtherSnapshot(), T0.AddDays(5));
        var older = await AddRunAsync(db, suiteId, BaselineSnapshot(), T0.AddDays(1));
        var comparable = await AddRunAsync(db, suiteId, BaselineSnapshot(), T0.AddDays(2));
        await AddRunAsync(db, suiteId, BaselineSnapshot(), T0.AddDays(3), harness: "h2");
        await AddRunAsync(db, suiteId, BaselineSnapshot(), T0.AddDays(4), status: BenchmarkRunStatus.Failed);
        await AddRunAsync(db, suiteId, OtherSnapshot(), T0.AddDays(6));

        var finder = new BenchmarkBaselineModelSourceFinder(db);
        var source = await finder.FindSuiteRunAsync(BaselineKey, suiteId, await MembersAsync(db, member), new[] { member.Id }, ct);

        Assert.NotNull(source);
        Assert.Equal(comparable.Id, source!.RunId);
        Assert.Null(source.BatteryRunId);
        Assert.Equal($"Luna, run #{comparable.Id}", source.Label);
        Assert.Equal(comparable.StartedAtUtc, source.StartedAtUtc);
        Assert.NotEqual(older.Id, source.RunId);
    }

    [Fact]
    public async Task Suite_TriesAtMostTenRuns()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateDb();
        long suiteId = await AddSuiteAsync(db);
        var member = await AddRunAsync(db, suiteId, OtherSnapshot(), T0.AddDays(20));
        var comparable = await AddRunAsync(db, suiteId, BaselineSnapshot(), T0);
        for (int i = 1; i <= BenchmarkBaselineModelSourceFinder.MaxSuiteCandidates; i++)
        {
            await AddRunAsync(db, suiteId, BaselineSnapshot(), T0.AddDays(i), harness: "h2");
        }

        var finder = new BenchmarkBaselineModelSourceFinder(db);
        Assert.Null(await finder.FindSuiteRunAsync(BaselineKey, suiteId, await MembersAsync(db, member), new[] { member.Id }, ct));

        // One fewer non-comparable run brings the comparable one within the ten tries.
        var newest = await db.BenchmarkRuns.OrderByDescending(r => r.StartedAtUtc).FirstAsync(r => r.HarnessVersion == "h2", ct);
        db.BenchmarkRuns.Remove(newest);
        await db.SaveChangesAsync(ct);

        var source = await finder.FindSuiteRunAsync(BaselineKey, suiteId, await MembersAsync(db, member), new[] { member.Id }, ct);
        Assert.Equal(comparable.Id, source?.RunId);
    }

    [Fact]
    public async Task Suite_FindsNothing_WithoutABaselineRun()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateDb();
        long suiteId = await AddSuiteAsync(db);
        var member = await AddRunAsync(db, suiteId, OtherSnapshot(), T0.AddDays(5));
        await AddRunAsync(db, suiteId, OtherSnapshot(), T0.AddDays(1));
        await AddRunAsync(db, suiteId + 1, BaselineSnapshot(), T0.AddDays(2));

        var finder = new BenchmarkBaselineModelSourceFinder(db);
        Assert.Null(await finder.FindSuiteRunAsync(BaselineKey, suiteId, await MembersAsync(db, member), new[] { member.Id }, ct));
    }

    // --- Battery ----------------------------------------------------------------------------------

    private static async Task<BenchmarkBatteryRun> AddBatteryRunAsync(
        ApplicationDbContext db,
        long suiteId,
        DateTime startedAtUtc,
        string comparabilityClass = "class-1",
        bool complete = true,
        string definition = "def-1",
        params SystemAiConfigurationSnapshot[] memberModels)
    {
        var ct = TestContext.Current.CancellationToken;
        var batteryRun = new BenchmarkBatteryRun
        {
            BatteryName = "Battery",
            DefinitionJson = "{}",
            DefinitionSha256 = definition,
            StartRequestJson = "{}",
            RunsPerSuite = 1,
            RequestedMemberCount = memberModels.Length,
            CompletedMemberCount = memberModels.Length,
            Status = BenchmarkRunSeriesStatus.Completed,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = startedAtUtc.AddHours(1)
        };
        db.BenchmarkBatteryRuns.Add(batteryRun);
        await db.SaveChangesAsync(ct);

        var runIds = new List<long>();
        for (int i = 0; i < memberModels.Length; i++)
        {
            var run = await AddRunAsync(db, suiteId, memberModels[i], startedAtUtc.AddMinutes(i));
            runIds.Add(run.Id);
            db.BenchmarkBatteryRunMembers.Add(new BenchmarkBatteryRunMember
            {
                BenchmarkBatteryRunId = batteryRun.Id, BenchmarkRunId = run.Id, SuiteIndex = i, Round = 1, AddedAtUtc = startedAtUtc
            });
        }

        db.BenchmarkBatteryAnalyses.Add(new BenchmarkBatteryAnalysis
        {
            BenchmarkBatteryRunId = batteryRun.Id,
            ComputedAtUtc = startedAtUtc.AddHours(2),
            MemberRunIdsJson = "[" + string.Join(",", runIds) + "]",
            ResultJson = "{}",
            DefinitionSha256 = definition,
            ComparabilityClassSha256 = comparabilityClass,
            Complete = complete,
            ScoringMethodVersion = 1
        });
        await db.SaveChangesAsync(ct);
        return batteryRun;
    }

    [Fact]
    public async Task Battery_FindsTheNewestRankedBatteryRunOfTheMembersClass_WhoseNewestMemberRunIsTheBaseline()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateDb();
        long suiteId = await AddSuiteAsync(db);
        var member = await AddBatteryRunAsync(db, suiteId, T0.AddDays(10), memberModels: new[] { OtherSnapshot() });
        await AddBatteryRunAsync(db, suiteId, T0.AddDays(1), memberModels: new[] { BaselineSnapshot() });
        var expected = await AddBatteryRunAsync(db, suiteId, T0.AddDays(2), memberModels: new[] { OtherSnapshot(), BaselineSnapshot() });
        await AddBatteryRunAsync(db, suiteId, T0.AddDays(3), comparabilityClass: "class-2", memberModels: new[] { BaselineSnapshot() });
        await AddBatteryRunAsync(db, suiteId, T0.AddDays(4), complete: false, memberModels: new[] { BaselineSnapshot() });
        await AddBatteryRunAsync(db, suiteId, T0.AddDays(5), definition: "def-2", memberModels: new[] { BaselineSnapshot() });
        await AddBatteryRunAsync(db, suiteId, T0.AddDays(6), memberModels: new[] { BaselineSnapshot(), OtherSnapshot() });

        var finder = new BenchmarkBaselineModelSourceFinder(db);
        var source = await finder.FindBatteryRunAsync(BaselineKey, new[] { member.Id }, ct);

        Assert.NotNull(source);
        Assert.Equal(expected.Id, source!.BatteryRunId);
        Assert.Null(source.RunId);
        Assert.Equal($"Luna, battery run #{expected.Id}", source.Label);
        Assert.Equal(expected.StartedAtUtc, source.StartedAtUtc);
    }

    [Fact]
    public async Task Battery_FindsNothing_WithoutARankedBaselineResult()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateDb();
        long suiteId = await AddSuiteAsync(db);
        var member = await AddBatteryRunAsync(db, suiteId, T0.AddDays(10), memberModels: new[] { OtherSnapshot() });
        await AddBatteryRunAsync(db, suiteId, T0.AddDays(1), complete: false, memberModels: new[] { BaselineSnapshot() });
        await AddBatteryRunAsync(db, suiteId, T0.AddDays(2), memberModels: new[] { OtherSnapshot() });

        var finder = new BenchmarkBaselineModelSourceFinder(db);
        Assert.Null(await finder.FindBatteryRunAsync(BaselineKey, new[] { member.Id }, ct));
    }
}
