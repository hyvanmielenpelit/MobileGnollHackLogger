namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MobileGnollHackLogger.Data;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// The caps and the rolling-window counts the run-count field bounds itself by.
///
/// <para>The window is <b>rolling</b> — the last 60 minutes and the last 24 hours, counted from now
/// — and not a calendar hour or a calendar day. That distinction is the whole point of these tests:
/// a client that re-derived "today" as midnight-to-now would disagree with the guard that actually
/// refuses the run, and the disagreement would only show up as a series that stopped for a reason
/// the UI said could not happen.</para>
/// </summary>
public class BenchmarkRunLimitsTests
{
    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static IConfiguration CreateConfig(int maxRunsPerDay = 20, int maxRunsPerHour = 5)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Benchmark:Compliance:MaxRunsPerDay", maxRunsPerDay.ToString() },
                { "Benchmark:Compliance:MaxRunsPerHour", maxRunsPerHour.ToString() }
            })
            .Build();

    private static void AddRunStartedHoursAgo(ApplicationDbContext db, double hours)
    {
        db.BenchmarkRuns.Add(new BenchmarkRun
        {
            SuiteName = "Suite",
            // The model columns are required and irrelevant here: the guard counts rows by
            // StartedAtUtc alone. Filled so the fixture saves, not because their values matter.
            TestedModelDisplayNameUsed = "Candidate",
            TestedModelProviderUsed = "TestProvider",
            TestedModelIdUsed = "candidate-model",
            AssessorModelDisplayNameUsed = "Assessor",
            AssessorModelProviderUsed = "TestProvider",
            AssessorModelIdUsed = "assessor-model",
            StartedAtUtc = DateTime.UtcNow.AddHours(-hours),
            Status = BenchmarkRunStatus.Completed
        });
    }

    [Fact]
    public async Task Limits_CountTheRolling24Hours_NotTheCalendarDay()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateDbContext();

        AddRunStartedHoursAgo(db, 23);   // inside the window
        AddRunStartedHoursAgo(db, 25);   // outside it
        AddRunStartedHoursAgo(db, 0.5);  // inside both windows
        await db.SaveChangesAsync(ct);

        var guard = new BenchmarkComplianceGuard(CreateConfig(), db);
        var limits = await guard.GetLimitsAsync(ct: ct);

        Assert.Equal(2, limits.RunsInLast24Hours);
        Assert.Equal(1, limits.RunsInLastHour);
    }

    [Fact]
    public async Task Limits_CountTheRolling60Minutes_ForTheHourlyWindow()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateDbContext();

        AddRunStartedHoursAgo(db, 0.5);
        AddRunStartedHoursAgo(db, 0.9);
        AddRunStartedHoursAgo(db, 1.1);  // just outside
        await db.SaveChangesAsync(ct);

        var guard = new BenchmarkComplianceGuard(CreateConfig(), db);
        var limits = await guard.GetLimitsAsync(ct: ct);

        Assert.Equal(2, limits.RunsInLastHour);
        Assert.Equal(3, limits.RunsInLast24Hours);
    }

    [Fact]
    public async Task Limits_ReportTheConfiguredCaps_NotCompiledDefaults()
    {
        using var db = CreateDbContext();

        var guard = new BenchmarkComplianceGuard(CreateConfig(maxRunsPerDay: 7, maxRunsPerHour: 3), db);
        var limits = await guard.GetLimitsAsync(ct: TestContext.Current.CancellationToken);

        Assert.Equal(7, limits.MaxRunsPerDay);
        Assert.Equal(3, limits.MaxRunsPerHour);
        Assert.Equal(7, limits.RemainingDailyHeadroom);
    }

    /// <summary>
    /// The daily count can exceed the cap after an operator lowers the setting. A negative headroom
    /// would render as a negative maximum on the run-count field, so it is clamped at zero.
    /// </summary>
    [Fact]
    public async Task RemainingDailyHeadroom_NeverGoesNegative()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateDbContext();

        for (int i = 0; i < 5; i++)
        {
            AddRunStartedHoursAgo(db, 2);
        }
        await db.SaveChangesAsync(ct);

        var guard = new BenchmarkComplianceGuard(CreateConfig(maxRunsPerDay: 2), db);
        var limits = await guard.GetLimitsAsync(ct: ct);

        Assert.Equal(5, limits.RunsInLast24Hours);
        Assert.Equal(0, limits.RemainingDailyHeadroom);
    }

    [Fact]
    public async Task RemainingDailyHeadroom_IsTheCapMinusTheRollingCount()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateDbContext();

        AddRunStartedHoursAgo(db, 3);
        AddRunStartedHoursAgo(db, 30);  // outside the window: must not consume headroom
        await db.SaveChangesAsync(ct);

        var guard = new BenchmarkComplianceGuard(CreateConfig(maxRunsPerDay: 6), db);
        var limits = await guard.GetLimitsAsync(ct: ct);

        Assert.Equal(1, limits.RunsInLast24Hours);
        Assert.Equal(5, limits.RemainingDailyHeadroom);
    }

    /// <summary>
    /// The limits snapshot and <see cref="BenchmarkComplianceGuard.CanSpendAsync"/> must agree about
    /// the same database, because the field bounds itself by the former and the run is refused by
    /// the latter. One owner for the window arithmetic is the point.
    /// </summary>
    [Fact]
    public async Task Limits_AgreeWithCanSpend_AtTheBoundary()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateDbContext();

        AddRunStartedHoursAgo(db, 2);
        AddRunStartedHoursAgo(db, 3);
        await db.SaveChangesAsync(ct);

        var guard = new BenchmarkComplianceGuard(CreateConfig(maxRunsPerDay: 2, maxRunsPerHour: 10), db);

        var limits = await guard.GetLimitsAsync(ct: ct);
        var (allowed, reason) = await guard.CanSpendAsync(ct: ct);

        Assert.Equal(0, limits.RemainingDailyHeadroom);
        Assert.False(allowed);
        Assert.Contains("Daily", reason);
    }
}
