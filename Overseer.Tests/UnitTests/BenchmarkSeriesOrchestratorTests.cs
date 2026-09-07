namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// Series execution, resume and the rolling-window limits.
///
/// <para>These tests deliberately drive the orchestrator's <b>decisions</b> rather than a live
/// series: actually executing a member requires a candidate model, an assessor and roughly half an
/// hour, none of which belongs in a unit test. What is testable here — and what the plan's risk
/// table is actually about — is every branch that decides whether a member launches at all: the
/// run-count bound, the cap behaviour, the resume refusals, the instrument guard, and the startup
/// reconciliation that makes a resume possible after a crash.</para>
/// </summary>
public class BenchmarkSeriesOrchestratorTests
{
    private static ApplicationDbContext CreateDbContext(string? name = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: name ?? Guid.NewGuid().ToString())
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

    /// <summary>
    /// A scope factory over one shared in-memory database, so the orchestrator's per-member scopes
    /// all see the same rows the test wrote.
    /// </summary>
    private static (IServiceScopeFactory Factory, string DbName) CreateScopeFactory(IConfiguration config)
    {
        string dbName = Guid.NewGuid().ToString();

        var services = new ServiceCollection();
        services.AddSingleton(config);
        services.AddScoped(_ => CreateDbContext(dbName));
        services.AddScoped<BenchmarkComplianceGuard>();
        services.AddSingleton<BenchmarkRunManager>();
        services.AddSingleton<BenchmarkDifficultyJobManager>();

        // The resume path's instrument guard resolves BenchmarkService to recompute the three
        // hashes. The fixture's series record no member-1 hashes, so the guard short-circuits
        // before the fingerprint is computed and the chat service is never touched — which is why
        // these two dependencies can be null. A fixture that did record them would need a real
        // ChatService here.
        //
        // Registering it is deliberate rather than making the guard tolerate its absence: a
        // missing service must never be read as "the instrument did not move".
        services.AddScoped(sp => new BenchmarkService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            null!,
            null!,
            new CryptoService(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "AesEncryptionKey", Convert.ToBase64String(new byte[32]) }
                }).Build()),
            sp.GetRequiredService<BenchmarkRunManager>(),
            sp.GetRequiredService<BenchmarkDifficultyJobManager>(),
            new BenchmarkScoringProfileService(
                sp.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<BenchmarkScoringProfileService>.Instance),
            config,
            NullLogger<BenchmarkService>.Instance));

        // StartSeriesAsync validates the request through the same launcher a single run is admitted
        // by, so a request the launcher would reject never creates a series. That resolution happens
        // in the orchestrator's own scope, which is this one.
        services.AddScoped<BenchmarkRunLauncher>();

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IServiceScopeFactory>(), dbName);
    }

    private static BenchmarkSeriesOrchestrator CreateOrchestrator(
        IServiceScopeFactory factory, BenchmarkRunManager runManager)
        => new(factory, runManager, NullLogger<BenchmarkSeriesOrchestrator>.Instance);

    private static async Task<BenchmarkSuite> SeedSuiteAndConfigsAsync(ApplicationDbContext db)
    {
        var suite = new BenchmarkSuite { Name = "Test Suite", Description = "Desc" };
        suite.Questions.Add(new BenchmarkQuestion
        {
            QuestionText = "Q1",
            OrderIndex = 1,
            Difficulty = BenchmarkDifficulty.Simple,
            AssessedDifficulty = 25
        });
        db.BenchmarkSuites.Add(suite);

        // StartSeriesAsync validates the whole request up front, through the same launcher a single
        // run is admitted by, so a series is never created for a request that would fail at member 1.
        // That makes admissible model configurations part of the fixture rather than an optional
        // extra: without them every start would return Invalid before reaching the code under test.
        //
        // The ids match the Request() helper. Two different providers, because an identical pair
        // would return SameProviderNotAcknowledged instead.
        db.SystemAiApiConfigurations.Add(new SystemAiApiConfiguration
        {
            Id = 1,
            Provider = "Anthropic",
            ModelId = "claude-opus-5",
            DisplayName = "Tested Model",
            IsEnabled = true,
            EncryptedApiKey = "encrypted-tested-key",
            ModelRole = 4
        });
        db.SystemAiApiConfigurations.Add(new SystemAiApiConfiguration
        {
            Id = 2,
            Provider = "OpenAI",
            ModelId = "gpt-5",
            DisplayName = "Assessor Model",
            IsEnabled = true,
            EncryptedApiKey = "encrypted-assessor-key",
            ModelRole = 4
        });

        await db.SaveChangesAsync();
        return suite;
    }

    private static StartBenchmarkRunRequest Request(long suiteId, int runCount) => new()
    {
        SuiteId = suiteId,
        TestedModelConfigurationId = 1,
        AssessorModelConfigurationId = 2,
        RunCount = runCount
    };

    // --- Run count is bounded by the live configured cap -------------------------------------

    [Fact]
    public async Task StartSeries_RejectsARunCountBelowOne()
    {
        var config = CreateConfig(maxRunsPerDay: 20);
        var (factory, dbName) = CreateScopeFactory(config);
        using var db = CreateDbContext(dbName);
        var suite = await SeedSuiteAndConfigsAsync(db);

        var orchestrator = CreateOrchestrator(factory, new BenchmarkRunManager());

        var result = await orchestrator.StartSeriesAsync(Request(suite.Id, runCount: 0), "user");

        Assert.Equal(BenchmarkSeriesStartOutcome.Invalid, result.Outcome);
        Assert.Contains("at least 1", result.Error);
    }

    /// <summary>
    /// The bound is read from configuration, not hard-coded, so this test moves with the setting
    /// rather than pinning a number the operator is free to change.
    ///
    /// <para>A series of exactly <c>MaxRunsPerDay</c> is <b>accepted</b>, and the arithmetic works
    /// out: the guard tests the count <i>before</i> creating each run, so from an empty window the
    /// last member sees a count of <i>N</i> − 1 and passes.</para>
    /// </summary>
    [Theory]
    [InlineData(6)]
    [InlineData(20)]
    public async Task StartSeries_AcceptsExactlyTheConfiguredDailyCap_AndRejectsOneMore(int maxRunsPerDay)
    {
        var config = CreateConfig(maxRunsPerDay: maxRunsPerDay, maxRunsPerHour: maxRunsPerDay);
        var (factory, dbName) = CreateScopeFactory(config);
        using var db = CreateDbContext(dbName);
        var suite = await SeedSuiteAndConfigsAsync(db);

        var orchestrator = CreateOrchestrator(factory, new BenchmarkRunManager());

        var tooMany = await orchestrator.StartSeriesAsync(
            Request(suite.Id, runCount: maxRunsPerDay + 1), "user");

        Assert.Equal(BenchmarkSeriesStartOutcome.Invalid, tooMany.Outcome);
        Assert.Contains(maxRunsPerDay.ToString(), tooMany.Error);

        var atCap = await orchestrator.StartSeriesAsync(
            Request(suite.Id, runCount: maxRunsPerDay), "user");

        Assert.Equal(BenchmarkSeriesStartOutcome.Started, atCap.Outcome);
    }

    [Fact]
    public async Task StartSeries_IsRefused_WhileARunIsAlreadyInFlight()
    {
        var config = CreateConfig();
        var (factory, dbName) = CreateScopeFactory(config);
        using var db = CreateDbContext(dbName);
        var suite = await SeedSuiteAndConfigsAsync(db);

        // The single-run gate is the reason a series is strictly sequential. A series that could
        // start while a run was live would put two runs in flight, which is exactly what
        // BenchmarkRunManager exists to prevent.
        var runManager = new BenchmarkRunManager();
        Assert.True(runManager.TryStart(999, new System.Threading.CancellationTokenSource(), out _));

        var orchestrator = CreateOrchestrator(factory, runManager);

        var result = await orchestrator.StartSeriesAsync(Request(suite.Id, runCount: 2), "user");

        Assert.Equal(BenchmarkSeriesStartOutcome.Conflict, result.Outcome);
    }

    [Fact]
    public async Task StartSeries_RecordsTheRequestAndTheCapPreference_OnTheRow()
    {
        var config = CreateConfig();
        var (factory, dbName) = CreateScopeFactory(config);
        using var db = CreateDbContext(dbName);
        var suite = await SeedSuiteAndConfigsAsync(db);

        var request = Request(suite.Id, runCount: 3);
        request.AllowCapWait = true;

        var orchestrator = CreateOrchestrator(factory, new BenchmarkRunManager());
        var result = await orchestrator.StartSeriesAsync(request, "user");

        Assert.True(result.Started);

        using var readback = CreateDbContext(dbName);
        var series = await readback.BenchmarkRunSeries.FirstAsync(s => s.Id == result.SeriesId!.Value);

        Assert.Equal(3, series.RequestedRunCount);
        Assert.Equal(0, series.CompletedRunCount);
        Assert.True(series.AllowCapWait);

        // Every member is launched from this one snapshot rather than from live UI state, so the
        // members cannot drift apart between the first launch and a resume days later.
        var stored = BenchmarkSeriesOrchestrator.DeserializeRequest(series);
        Assert.NotNull(stored);
        Assert.Equal(suite.Id, stored!.SuiteId);
        Assert.Equal(3, stored.RunCount);
    }

    // --- Resume ------------------------------------------------------------------------------

    private static async Task<BenchmarkRunSeries> SeedSeriesAsync(
        ApplicationDbContext db,
        BenchmarkSuite suite,
        BenchmarkRunSeriesStatus status,
        int requested = 3,
        int completed = 1,
        BenchmarkRunSeriesStopReason? stopReason = null)
    {
        var series = new BenchmarkRunSeries
        {
            BenchmarkSuiteId = suite.Id,
            SuiteName = suite.Name,
            RequestedRunCount = requested,
            CompletedRunCount = completed,
            Status = status,
            StopReason = stopReason,
            StartRequestJson = JsonSerializer.Serialize(Request(suite.Id, requested)),
            StartedAtUtc = DateTime.UtcNow.AddHours(-2)
        };

        db.BenchmarkRunSeries.Add(series);
        await db.SaveChangesAsync();
        return series;
    }

    [Theory]
    [InlineData(BenchmarkRunSeriesStatus.Cancelled)]
    [InlineData(BenchmarkRunSeriesStatus.Completed)]
    [InlineData(BenchmarkRunSeriesStatus.Failed)]
    public async Task Resume_IsRefused_ForATerminalSeries(BenchmarkRunSeriesStatus status)
    {
        var config = CreateConfig();
        var (factory, dbName) = CreateScopeFactory(config);
        using var db = CreateDbContext(dbName);
        var suite = await SeedSuiteAndConfigsAsync(db);
        var series = await SeedSeriesAsync(db, suite, status);

        var orchestrator = CreateOrchestrator(factory, new BenchmarkRunManager());

        var result = await orchestrator.ResumeSeriesAsync(series.Id, acknowledgeInstrumentChange: false);

        // Cancelled is the important one: the operator said stop, which is a different statement
        // from a series that halted on its own and left a Continue button.
        Assert.Equal(BenchmarkSeriesStartOutcome.Invalid, result.Outcome);
        Assert.Contains("cannot be resumed", result.Error);
    }

    [Fact]
    public async Task Resume_IsRefused_WhenEveryRequestedMemberHasAlreadyCompleted()
    {
        var config = CreateConfig();
        var (factory, dbName) = CreateScopeFactory(config);
        using var db = CreateDbContext(dbName);
        var suite = await SeedSuiteAndConfigsAsync(db);
        var series = await SeedSeriesAsync(
            db, suite, BenchmarkRunSeriesStatus.Stopped, requested: 3, completed: 3);

        var orchestrator = CreateOrchestrator(factory, new BenchmarkRunManager());

        var result = await orchestrator.ResumeSeriesAsync(series.Id, acknowledgeInstrumentChange: false);

        Assert.Equal(BenchmarkSeriesStartOutcome.Invalid, result.Outcome);
        Assert.Contains("already completed", result.Error);
    }

    [Fact]
    public async Task Resume_IsRefused_WhileARunIsInFlight()
    {
        var config = CreateConfig();
        var (factory, dbName) = CreateScopeFactory(config);
        using var db = CreateDbContext(dbName);
        var suite = await SeedSuiteAndConfigsAsync(db);
        var series = await SeedSeriesAsync(db, suite, BenchmarkRunSeriesStatus.Stopped);

        // Resume goes through the same single-run gate as any other launch, so pressing Continue
        // twice — or pressing it while another run is live — cannot double-start a member.
        var runManager = new BenchmarkRunManager();
        Assert.True(runManager.TryStart(999, new System.Threading.CancellationTokenSource(), out _));

        var orchestrator = CreateOrchestrator(factory, runManager);

        var result = await orchestrator.ResumeSeriesAsync(series.Id, acknowledgeInstrumentChange: false);

        Assert.Equal(BenchmarkSeriesStartOutcome.Conflict, result.Outcome);
    }

    [Fact]
    public async Task Resume_IsRefused_WhenTheSpendGuardDenies()
    {
        // A cap of one, with one run already inside the rolling window: the guard refuses before
        // the instrument guard is even consulted. A resume is a new run and is capped like one.
        var config = CreateConfig(maxRunsPerDay: 1, maxRunsPerHour: 1);
        var (factory, dbName) = CreateScopeFactory(config);
        using var db = CreateDbContext(dbName);
        var suite = await SeedSuiteAndConfigsAsync(db);
        var series = await SeedSeriesAsync(db, suite, BenchmarkRunSeriesStatus.Stopped);

        db.BenchmarkRuns.Add(new BenchmarkRun
        {
            BenchmarkSuiteId = suite.Id,
            SuiteName = suite.Name,
            TestedModelDisplayNameUsed = "Candidate",
            TestedModelProviderUsed = "TestProvider",
            TestedModelIdUsed = "candidate-model",
            AssessorModelDisplayNameUsed = "Assessor",
            AssessorModelProviderUsed = "TestProvider",
            AssessorModelIdUsed = "assessor-model",
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            Status = BenchmarkRunStatus.Completed
        });
        await db.SaveChangesAsync();

        var orchestrator = CreateOrchestrator(factory, new BenchmarkRunManager());

        var result = await orchestrator.ResumeSeriesAsync(series.Id, acknowledgeInstrumentChange: false);

        Assert.Equal(BenchmarkSeriesStartOutcome.SpendDenied, result.Outcome);
    }

    [Fact]
    public async Task Resume_OfAnUnknownSeries_IsNotFound()
    {
        var config = CreateConfig();
        var (factory, _) = CreateScopeFactory(config);

        var orchestrator = CreateOrchestrator(factory, new BenchmarkRunManager());

        var result = await orchestrator.ResumeSeriesAsync(4242, acknowledgeInstrumentChange: false);

        Assert.Equal(BenchmarkSeriesStartOutcome.NotFound, result.Outcome);
    }

    // --- Cancellation ------------------------------------------------------------------------

    [Fact]
    public async Task Cancel_MarksTheSeriesCancelled_AndCancellationIsNotResumable()
    {
        var config = CreateConfig();
        var (factory, dbName) = CreateScopeFactory(config);
        using var db = CreateDbContext(dbName);
        var suite = await SeedSuiteAndConfigsAsync(db);
        var series = await SeedSeriesAsync(db, suite, BenchmarkRunSeriesStatus.Running);

        var orchestrator = CreateOrchestrator(factory, new BenchmarkRunManager());

        Assert.True(await orchestrator.CancelSeriesAsync(series.Id));

        using var readback = CreateDbContext(dbName);
        var cancelled = await readback.BenchmarkRunSeries.FirstAsync(s => s.Id == series.Id);
        Assert.Equal(BenchmarkRunSeriesStatus.Cancelled, cancelled.Status);
        Assert.Null(cancelled.StopReason);
        Assert.NotNull(cancelled.CompletedAtUtc);

        var resume = await orchestrator.ResumeSeriesAsync(series.Id, acknowledgeInstrumentChange: false);
        Assert.Equal(BenchmarkSeriesStartOutcome.Invalid, resume.Outcome);
    }

    [Fact]
    public async Task Cancel_CancelsTheInFlightMemberThroughTheRunManager()
    {
        var config = CreateConfig();
        var (factory, dbName) = CreateScopeFactory(config);
        using var db = CreateDbContext(dbName);
        var suite = await SeedSuiteAndConfigsAsync(db);
        var series = await SeedSeriesAsync(db, suite, BenchmarkRunSeriesStatus.Running);

        var member = new BenchmarkRun
        {
            BenchmarkSuiteId = suite.Id,
            SuiteName = suite.Name,
            TestedModelDisplayNameUsed = "Candidate",
            TestedModelProviderUsed = "TestProvider",
            TestedModelIdUsed = "candidate-model",
            AssessorModelDisplayNameUsed = "Assessor",
            AssessorModelProviderUsed = "TestProvider",
            AssessorModelIdUsed = "assessor-model",
            RunSeriesId = series.Id,
            RunSeriesIndex = 2,
            Status = BenchmarkRunStatus.Running,
            StartedAtUtc = DateTime.UtcNow
        };
        db.BenchmarkRuns.Add(member);
        await db.SaveChangesAsync();

        var runManager = new BenchmarkRunManager();
        var cts = new System.Threading.CancellationTokenSource();
        Assert.True(runManager.TryStart(member.Id, cts, out _));

        var orchestrator = CreateOrchestrator(factory, runManager);
        Assert.True(await orchestrator.CancelSeriesAsync(series.Id));

        // Cancelled through the run manager rather than by writing the row directly, so the run's
        // own finalisation path runs instead of being bypassed.
        Assert.True(cts.IsCancellationRequested);
        Assert.Null(runManager.CurrentRunId);
    }

    [Fact]
    public async Task Cancel_OfAnUnknownSeries_ReportsFailureRatherThanThrowing()
    {
        var config = CreateConfig();
        var (factory, _) = CreateScopeFactory(config);
        var orchestrator = CreateOrchestrator(factory, new BenchmarkRunManager());

        Assert.False(await orchestrator.CancelSeriesAsync(4242));
    }

    // --- Startup reconciliation --------------------------------------------------------------

    /// <summary>
    /// The orchestrator is in-memory; the series is a row. After an unclean shutdown nothing is
    /// advancing a <c>Running</c> series, so without this reconciliation the Continue button would
    /// never appear and the series would be permanently unresumable — defeating the reason the state
    /// was put in the database in the first place.
    /// </summary>
    [Fact]
    public async Task Reconcile_MovesAnOrphanedSeriesToStopped_SoContinueBecomesAvailable()
    {
        var config = CreateConfig();
        var (factory, dbName) = CreateScopeFactory(config);
        using var db = CreateDbContext(dbName);
        var suite = await SeedSuiteAndConfigsAsync(db);

        var running = await SeedSeriesAsync(db, suite, BenchmarkRunSeriesStatus.Running);
        var waiting = await SeedSeriesAsync(db, suite, BenchmarkRunSeriesStatus.WaitingForCap);
        var pending = await SeedSeriesAsync(db, suite, BenchmarkRunSeriesStatus.Pending);
        var completed = await SeedSeriesAsync(db, suite, BenchmarkRunSeriesStatus.Completed);
        var cancelled = await SeedSeriesAsync(db, suite, BenchmarkRunSeriesStatus.Cancelled);

        var orchestrator = CreateOrchestrator(factory, new BenchmarkRunManager());

        int reconciled = await orchestrator.ReconcileOrphanedSeriesAsync(db);

        Assert.Equal(3, reconciled);

        using var readback = CreateDbContext(dbName);
        foreach (long id in new[] { running.Id, waiting.Id, pending.Id })
        {
            var row = await readback.BenchmarkRunSeries.FirstAsync(s => s.Id == id);
            Assert.Equal(BenchmarkRunSeriesStatus.Stopped, row.Status);
            Assert.NotNull(row.StopReason);
            Assert.Contains("restarted", row.ErrorMessage);
        }

        // A terminal series is left exactly as it was: it is not orphaned, it is finished.
        Assert.Equal(
            BenchmarkRunSeriesStatus.Completed,
            (await readback.BenchmarkRunSeries.FirstAsync(s => s.Id == completed.Id)).Status);
        Assert.Equal(
            BenchmarkRunSeriesStatus.Cancelled,
            (await readback.BenchmarkRunSeries.FirstAsync(s => s.Id == cancelled.Id)).Status);
    }

    [Fact]
    public async Task Reconcile_IsANoOp_WhenNothingIsOrphaned()
    {
        var config = CreateConfig();
        var (factory, dbName) = CreateScopeFactory(config);
        using var db = CreateDbContext(dbName);

        var orchestrator = CreateOrchestrator(factory, new BenchmarkRunManager());

        Assert.Equal(0, await orchestrator.ReconcileOrphanedSeriesAsync(db));
    }

    /// <summary>
    /// A reconciled series is resumed by a <b>freshly constructed</b> orchestrator, which is the
    /// property that makes resume survive a process restart: nothing in the resume path depends on
    /// the instance that started the series.
    /// </summary>
    [Fact]
    public async Task Resume_WorksFromAFreshOrchestrator_AfterReconciliation()
    {
        var config = CreateConfig();
        var (factory, dbName) = CreateScopeFactory(config);
        using var db = CreateDbContext(dbName);
        var suite = await SeedSuiteAndConfigsAsync(db);
        var series = await SeedSeriesAsync(db, suite, BenchmarkRunSeriesStatus.Running);

        await CreateOrchestrator(factory, new BenchmarkRunManager()).ReconcileOrphanedSeriesAsync(db);

        // A different instance entirely — as after a service restart.
        var afterRestart = CreateOrchestrator(factory, new BenchmarkRunManager());

        var result = await afterRestart.ResumeSeriesAsync(series.Id, acknowledgeInstrumentChange: false);

        // What this asserts is that the resume was *accepted* by an orchestrator that never saw the
        // series start. The refusals above are the branches that reject before this point.
        Assert.Equal(BenchmarkSeriesStartOutcome.Started, result.Outcome);
    }

    /// <summary>
    /// The Tier A assertion in <c>CreateGroupForSeriesAsync</c>, tested at its decision input.
    ///
    /// <para>Members of one series are launched minutes apart from a single request, so their
    /// pricing snapshots agree on every price and disagree on the instant they were taken. That is
    /// the condition Sentry OVERSEER-8 reported for series 2, and it must resolve <b>Tier A</b>: a
    /// series that logs the group-creation invariant error is either a harness defect or a
    /// mid-series catalog edit, and neither applies to two runs of one request.</para>
    ///
    /// <para>Asserted against <c>BenchmarkComparabilityKey.Resolve</c> over series-shaped members
    /// rather than by driving the private method: reaching it needs a member to actually execute,
    /// which is the half-hour of candidate and assessor calls this file's header explains it does
    /// not do.</para>
    /// </summary>
    [Fact]
    public async Task SeriesMembers_PricedAtDifferentInstants_ResolveTierA()
    {
        var config = CreateConfig();
        var (factory, dbName) = CreateScopeFactory(config);
        using var db = CreateDbContext(dbName);
        var suite = await SeedSuiteAndConfigsAsync(db);
        var series = await SeedSeriesAsync(db, suite, BenchmarkRunSeriesStatus.Running);

        // What BuildPricingSnapshotJson writes: one capturedAtUtc, then the resolved price cards.
        const string Prices = "\"candidate\":{\"inputPerMillion\":1.25,\"outputPerMillion\":10.0}";

        var members = new List<BenchmarkRun>();
        for (int index = 1; index <= 2; index++)
        {
            var member = new BenchmarkRun
            {
                BenchmarkSuiteId = suite.Id,
                SuiteName = suite.Name,
                TestedModelDisplayNameUsed = "Candidate",
                TestedModelProviderUsed = "TestProvider",
                TestedModelIdUsed = "candidate-model",
                AssessorModelDisplayNameUsed = "Assessor",
                AssessorModelProviderUsed = "TestProvider",
                AssessorModelIdUsed = "assessor-model",
                RunSeriesId = series.Id,
                RunSeriesIndex = index,
                Status = BenchmarkRunStatus.Completed,
                StartedAtUtc = DateTime.UtcNow.AddMinutes(-40 * index),
                HarnessVersion = "12",
                ScoringMethodVersion = 8,
                PricingSnapshotJson =
                    $"{{\"capturedAtUtc\":\"2026-09-07T0{7 + index}:46:51.283Z\",{Prices}}}"
            };
            members.Add(member);
            db.BenchmarkRuns.Add(member);
        }

        await db.SaveChangesAsync();

        var comparability = BenchmarkComparabilityKey.Resolve(members);

        Assert.Equal(BenchmarkComparabilityTier.Replicate, comparability.Tier);
        Assert.Empty(comparability.Differences);
        Assert.False(comparability.CostAggregatesDegraded);
        Assert.True(BenchmarkComparabilityKey.IsPoolable(comparability.Tier));
    }
}
