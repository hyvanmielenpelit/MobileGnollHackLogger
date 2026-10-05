namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.Benchmarking;
using Overseer.Tests.Helpers;
using Xunit;
using ParallelExecutionMode = MobileGnollHackLogger.Data.ParallelExecutionMode;

/// <summary>
/// Battery run execution: start, attaching existing runs and the reuse preview, resume, cancel,
/// startup reconciliation, reconciling after a member repair, and the decisions the drive loop makes
/// after each member.
///
/// <para>As for the series, the orchestrator's <b>decisions</b> are tested rather than a live
/// battery run: executing a member needs a candidate and an assessor. The two guards, the member
/// count and the resume pre-checks are static and are tested on loaded runs; start and resume are
/// driven through the orchestrator against an in-memory database, with the instrument fingerprint
/// supplied by the test.</para>
/// </summary>
public class BenchmarkBatteryOrchestratorTests
{
    // --- Fixture ------------------------------------------------------------------------------

    /// <summary>The orchestrator with the instrument fingerprint supplied by the test.</summary>
    private sealed class TestOrchestrator : BenchmarkBatteryOrchestrator
    {
        public TestOrchestrator(IServiceScopeFactory scopeFactory, BenchmarkRunManager runManager)
            : base(scopeFactory, runManager, NullLogger<BenchmarkBatteryOrchestrator>.Instance)
        {
        }

        public Func<long, BenchmarkInstrumentFingerprint?> Fingerprint { get; set; } = _ => null;

        protected override Task<BenchmarkInstrumentFingerprint?> ComputeFingerprintAsync(
            IServiceProvider services,
            ApplicationDbContext db,
            long suiteId,
            StartBenchmarkRunRequest request,
            CancellationToken ct)
            => Task.FromResult(Fingerprint(suiteId));
    }

    private static ApplicationDbContext CreateDbContext(string name)
        => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options);

    private static IConfiguration CreateConfig(int maxRunsPerDay = 20, int maxRunsPerHour = 5, int maxMembers = 60)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Benchmark:Compliance:MaxRunsPerDay", maxRunsPerDay.ToString() },
                { "Benchmark:Compliance:MaxRunsPerHour", maxRunsPerHour.ToString() },
                { "Benchmark:Battery:MaxMembers", maxMembers.ToString() }
            })
            .Build();

    /// <summary>A compliance guard that refuses every spend for a reason that is not a run cap.</summary>
    private sealed class OtherDenialGuard : BenchmarkComplianceGuard
    {
        public const string Reason = "Benchmark spending is suspended.";

        public OtherDenialGuard(IConfiguration configuration, ApplicationDbContext dbContext)
            : base(configuration, dbContext)
        {
        }

        public override Task<BenchmarkSpendCheck> CheckSpendAsync(ApplicationDbContext? db = null, CancellationToken ct = default)
            => Task.FromResult(BenchmarkSpendCheck.Deny(BenchmarkSpendDenialKind.Other, Reason));
    }

    /// <summary>A scope factory over one shared in-memory database, as the series tests build it.</summary>
    private static (IServiceScopeFactory Factory, string DbName) CreateScopeFactory(
        IConfiguration config,
        bool denySpendForAnotherReason = false)
    {
        string dbName = Guid.NewGuid().ToString();

        var services = new ServiceCollection();
        services.AddSingleton(config);
        services.AddScoped(_ => CreateDbContext(dbName));
        if (denySpendForAnotherReason)
        {
            services.AddScoped<BenchmarkComplianceGuard, OtherDenialGuard>();
        }
        else
        {
            services.AddScoped<BenchmarkComplianceGuard>();
        }
        services.AddSingleton<BenchmarkRunManager>();
        services.AddSingleton<BenchmarkDifficultyJobManager>();
        services.AddSingleton<Overseer.Services.Privacy.EndpointPolicy>();
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
            sp.GetRequiredService<Overseer.Services.Privacy.EndpointPolicy>(),
            config,
            NullLogger<BenchmarkService>.Instance));
        services.AddScoped<BenchmarkRunLauncher>();

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IServiceScopeFactory>(), dbName);
    }

    private sealed record Fixture(
        IServiceScopeFactory Factory,
        string DbName,
        BenchmarkRunManager RunManager,
        TestOrchestrator Orchestrator,
        BenchmarkBattery Battery,
        BenchmarkSuite SuiteA,
        BenchmarkSuite SuiteB);

    private static BenchmarkSuite Suite(string name, bool assessed = true)
    {
        var suite = new BenchmarkSuite { Name = name, Description = "Desc" };
        suite.Questions.Add(new BenchmarkQuestion
        {
            QuestionText = $"{name} Q1",
            OrderIndex = 1,
            Difficulty = BenchmarkDifficulty.Simple,
            AssessedDifficulty = assessed ? 40 : null
        });
        return suite;
    }

    /// <summary>
    /// Two suites, two admissible configurations of two providers (ids 1 and 2, as
    /// <see cref="RunRequest"/> names them), and a battery over both suites.
    /// </summary>
    private static async Task<Fixture> CreateFixtureAsync(
        IConfiguration? config = null,
        bool suiteBAssessed = true,
        bool archived = false,
        bool denySpendForAnotherReason = false)
    {
        var (factory, dbName) = CreateScopeFactory(config ?? CreateConfig(), denySpendForAnotherReason);
        using var db = CreateDbContext(dbName);

        var suiteA = Suite("Suite A");
        var suiteB = Suite("Suite B", suiteBAssessed);
        db.BenchmarkSuites.AddRange(suiteA, suiteB);

        db.SystemAiApiConfigurations.Add(new SystemAiApiConfiguration
        {
            Id = 1, Provider = "Anthropic", ModelId = "claude-opus-5", DisplayName = "Tested Model",
            IsEnabled = true, EncryptedApiKey = "encrypted-tested-key", ModelRole = 4
        });
        db.SystemAiApiConfigurations.Add(new SystemAiApiConfiguration
        {
            Id = 2, Provider = "OpenAI", ModelId = "gpt-5", DisplayName = "Assessor Model",
            IsEnabled = true, EncryptedApiKey = "encrypted-assessor-key", ModelRole = 4
        });
        await db.SaveChangesAsync();

        var battery = new BenchmarkBattery
        {
            Name = "Two Suites",
            WeightingScheme = BenchmarkBatteryWeightingScheme.DifficultyMass,
            IsArchived = archived,
            DefinitionSha256 = string.Empty
        };
        battery.Suites.Add(new BenchmarkBatterySuite { BenchmarkSuiteId = suiteA.Id, SuiteName = suiteA.Name, OrderIndex = 0 });
        battery.Suites.Add(new BenchmarkBatterySuite { BenchmarkSuiteId = suiteB.Id, SuiteName = suiteB.Name, OrderIndex = 1 });
        battery.DefinitionSha256 = BenchmarkBatteryDefinition.ComputeSha256(battery);
        db.BenchmarkBatteries.Add(battery);
        await db.SaveChangesAsync();

        var runManager = new BenchmarkRunManager();
        return new Fixture(factory, dbName, runManager, new TestOrchestrator(factory, runManager), battery, suiteA, suiteB);
    }

    private static StartBenchmarkRunRequest RunRequest() => new()
    {
        TestedModelConfigurationId = 1,
        AssessorModelConfigurationId = 2
    };

    private static StartBenchmarkBatteryRunRequest StartRequest(long batteryId, int runsPerSuite = 1, bool allowCapWait = false) => new()
    {
        BatteryId = batteryId,
        RunsPerSuite = runsPerSuite,
        AllowCapWait = allowCapWait,
        Run = RunRequest()
    };

    /// <summary>A battery run row over the fixture's battery, as a start would have written it.</summary>
    private static async Task<BenchmarkBatteryRun> SeedBatteryRunAsync(
        Fixture fixture,
        BenchmarkRunSeriesStatus status,
        int runsPerSuite = 1,
        string? fingerprintsJson = null,
        bool allowCapWait = false)
    {
        using var db = CreateDbContext(fixture.DbName);
        var battery = await db.BenchmarkBatteries.Include(b => b.Suites).SingleAsync(b => b.Id == fixture.Battery.Id);
        var definition = BenchmarkBatteryDefinition.FromEntity(battery);

        var stored = RunRequest();
        stored.SuiteId = fixture.SuiteA.Id;
        stored.AllowSourceCodeReferences = false;

        var batteryRun = new BenchmarkBatteryRun
        {
            BenchmarkBatteryId = battery.Id,
            BatteryName = battery.Name,
            DefinitionJson = definition.ToJson(),
            DefinitionSha256 = definition.DefinitionSha256,
            RunsPerSuite = runsPerSuite,
            RequestedMemberCount = definition.Suites.Count * runsPerSuite,
            Status = status,
            StopReason = status == BenchmarkRunSeriesStatus.Stopped ? BenchmarkRunSeriesStopReason.MemberFailed : null,
            StartRequestJson = JsonSerializer.Serialize(stored),
            AllowCapWait = allowCapWait,
            SuiteFingerprintsJson = fingerprintsJson,
            AutoCreatedGroupIdsJson = "{\"0\":5}",
            StartedAtUtc = DateTime.UtcNow.AddHours(-2)
        };

        db.BenchmarkBatteryRuns.Add(batteryRun);
        await db.SaveChangesAsync();
        return batteryRun;
    }

    /// <summary>One member of a seeded battery run, with its run.</summary>
    private static async Task<BenchmarkBatteryRunMember> SeedMemberAsync(
        Fixture fixture,
        BenchmarkBatteryRun batteryRun,
        int suiteIndex,
        int round = 1,
        BenchmarkRunStatus status = BenchmarkRunStatus.Completed,
        int? qualityIndex = 70,
        int? terminalFailures = null,
        int? scoringMethod = null,
        string? harnessVersion = null,
        string? guardFailure = null,
        string modelId = "claude-opus-5")
    {
        using var db = CreateDbContext(fixture.DbName);
        var suite = suiteIndex == 0 ? fixture.SuiteA : fixture.SuiteB;

        var run = new BenchmarkRun
        {
            BenchmarkSuiteId = suite.Id,
            BenchmarkSuiteIdUsed = suite.Id,
            SuiteName = suite.Name,
            TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Anthropic", modelId: modelId, displayName: modelId),
            AssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "OpenAI", modelId: "gpt-5", displayName: "Assessor"),
            Status = status,
            QualityIndex = qualityIndex,
            TerminalFailureAnswerCount = terminalFailures,
            ScoringMethodVersion = scoringMethod ?? BenchmarkAssessmentPrompt.ScoringMethodVersion,
            HarnessVersion = harnessVersion ?? BenchmarkAssessmentPrompt.HarnessVersion,
            StartedAtUtc = DateTime.UtcNow.AddHours(-30)
        };
        db.BenchmarkRuns.Add(run);
        await db.SaveChangesAsync();

        var member = new BenchmarkBatteryRunMember
        {
            BenchmarkBatteryRunId = batteryRun.Id,
            BenchmarkRunId = run.Id,
            SuiteIndex = suiteIndex,
            Round = round,
            Origin = BenchmarkBatteryMemberOrigin.Launched,
            GuardFailure = guardFailure
        };
        db.BenchmarkBatteryRunMembers.Add(member);
        await db.SaveChangesAsync();
        return member;
    }

    /// <summary>A run started five minutes ago, which fills an hourly or a daily cap of one.</summary>
    private static async Task FillCapAsync(Fixture fixture)
    {
        using var db = CreateDbContext(fixture.DbName);
        db.BenchmarkRuns.Add(BenchmarkModelSnapshots.Attach(new BenchmarkRun
        {
            BenchmarkSuiteId = fixture.SuiteA.Id,
            SuiteName = fixture.SuiteA.Name,
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            Status = BenchmarkRunStatus.Completed
        }));
        await db.SaveChangesAsync();
    }

    /// <summary>A third admissible configuration, for the report writer.</summary>
    private static async Task AddConfigurationAsync(Fixture fixture, long id, string provider, string modelId, string displayName)
    {
        using var db = CreateDbContext(fixture.DbName);
        db.SystemAiApiConfigurations.Add(new SystemAiApiConfiguration
        {
            Id = id, Provider = provider, ModelId = modelId, DisplayName = displayName,
            IsEnabled = true, EncryptedApiKey = "encrypted-writer-key", ModelRole = 4
        });
        await db.SaveChangesAsync();
    }

    // --- Start refusals -----------------------------------------------------------------------

    [Fact]
    public async Task Start_IsRefused_WhileARunIsInFlight()
    {
        var fixture = await CreateFixtureAsync();
        Assert.True(fixture.RunManager.TryStart(999, new CancellationTokenSource(), out _));

        var result = await fixture.Orchestrator.StartAsync(StartRequest(fixture.Battery.Id), "user", TestContext.Current.CancellationToken);

        Assert.Equal(BenchmarkBatteryStartOutcome.Conflict, result.Outcome);
    }

    [Fact]
    public async Task Start_IsRefused_WhileASeriesHoldsTheClaim_AndTakesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = await CreateFixtureAsync();
        Assert.True(fixture.RunManager.TryClaimOrchestrator(BenchmarkRunManager.SeriesOwner(3)));

        var result = await fixture.Orchestrator.StartAsync(StartRequest(fixture.Battery.Id), "user", ct);

        Assert.Equal(BenchmarkBatteryStartOutcome.Conflict, result.Outcome);
        Assert.Equal("A benchmark series is running; wait for it or cancel it.", result.Error);
        Assert.Equal(BenchmarkRunManager.SeriesOwner(3), fixture.RunManager.OrchestratorOwner);

        using var readback = CreateDbContext(fixture.DbName);
        Assert.Empty(await readback.BenchmarkBatteryRuns.ToListAsync(ct));
    }

    [Fact]
    public async Task Start_OfAnUnknownBattery_IsNotFound()
    {
        var fixture = await CreateFixtureAsync();

        var result = await fixture.Orchestrator.StartAsync(StartRequest(4242), "user", TestContext.Current.CancellationToken);

        Assert.Equal(BenchmarkBatteryStartOutcome.NotFound, result.Outcome);
    }

    [Fact]
    public async Task Start_OfAnArchivedBattery_IsInvalid()
    {
        var fixture = await CreateFixtureAsync(archived: true);

        var result = await fixture.Orchestrator.StartAsync(StartRequest(fixture.Battery.Id), "user", TestContext.Current.CancellationToken);

        Assert.Equal(BenchmarkBatteryStartOutcome.Invalid, result.Outcome);
        Assert.Contains("archived", result.Error);
    }

    [Fact]
    public async Task Start_OfABatteryWhoseSuiteWasDeleted_IsInvalid_NamingTheSuite()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = await CreateFixtureAsync();
        using (var db = CreateDbContext(fixture.DbName))
        {
            var row = await db.BenchmarkBatterySuites.SingleAsync(s => s.BenchmarkSuiteId == fixture.SuiteB.Id, ct);
            row.BenchmarkSuiteId = null;
            await db.SaveChangesAsync(ct);
        }

        var result = await fixture.Orchestrator.StartAsync(StartRequest(fixture.Battery.Id), "user", ct);

        Assert.Equal(BenchmarkBatteryStartOutcome.Invalid, result.Outcome);
        Assert.Contains("Suite B", result.Error);
    }

    [Fact]
    public async Task Start_WithRunsPerSuiteBelowOne_IsInvalid()
    {
        var fixture = await CreateFixtureAsync();

        var result = await fixture.Orchestrator.StartAsync(
            StartRequest(fixture.Battery.Id, runsPerSuite: 0), "user", TestContext.Current.CancellationToken);

        Assert.Equal(BenchmarkBatteryStartOutcome.Invalid, result.Outcome);
    }

    [Fact]
    public async Task Start_WithAnAttachedRunThatDoesNotExist_IsInvalid_NamingTheRun()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = await CreateFixtureAsync();
        var request = StartRequest(fixture.Battery.Id);
        request.Attach = new List<BenchmarkBatteryAttachDto> { new() { SuiteIndex = 0, Round = 1, RunId = 4242 } };

        var result = await fixture.Orchestrator.StartAsync(request, "user", ct);

        Assert.Equal(BenchmarkBatteryStartOutcome.Invalid, result.Outcome);
        Assert.Contains("Run #4242", result.Error);
        Assert.Null(fixture.RunManager.OrchestratorOwner);

        using var readback = CreateDbContext(fixture.DbName);
        Assert.Empty(await readback.BenchmarkBatteryRuns.ToListAsync(ct));
    }

    [Theory]
    [InlineData(2, 1, 10, "not in this battery run")]
    [InlineData(0, 1, 11, "named twice")]
    [InlineData(1, 1, 10, "two slots")]
    public void AttachList_RefusesASlotOutsideTheGrid_ASlotTwice_AndARunTwice(int suiteIndex, int round, long runId, string expected)
    {
        var attach = new List<BenchmarkBatteryAttachDto>
        {
            new() { SuiteIndex = 0, Round = 1, RunId = 10 },
            new() { SuiteIndex = suiteIndex, Round = round, RunId = runId }
        };

        Assert.Contains(expected, BenchmarkBatteryOrchestrator.AttachListRefusal(attach, suiteCount: 2, runsPerSuite: 1));
        Assert.Null(BenchmarkBatteryOrchestrator.AttachListRefusal(attach.Take(1).ToList(), suiteCount: 2, runsPerSuite: 1));
    }

    [Fact]
    public async Task Start_AboveTheMaximumMemberCount_IsTooManyMembers_EvenWithCapWait()
    {
        var fixture = await CreateFixtureAsync(CreateConfig(maxMembers: 3));

        var result = await fixture.Orchestrator.StartAsync(
            StartRequest(fixture.Battery.Id, runsPerSuite: 2, allowCapWait: true), "user", TestContext.Current.CancellationToken);

        Assert.Equal(BenchmarkBatteryStartOutcome.TooManyMembers, result.Outcome);
        Assert.Contains("4 launches", result.Error);
    }

    /// <summary>
    /// A battery run larger than the daily cap is refused without cap wait and accepted with it: the
    /// cap is never bypassed, because every member still passes the spend guard (decision 10).
    /// </summary>
    [Fact]
    public async Task Start_AboveTheDailyCap_IsRefusedWithoutCapWait_AndAcceptedWithIt()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = await CreateFixtureAsync(CreateConfig(maxRunsPerDay: 3, maxRunsPerHour: 3));

        var refused = await fixture.Orchestrator.StartAsync(StartRequest(fixture.Battery.Id, runsPerSuite: 2), "user", ct);
        Assert.Equal(BenchmarkBatteryStartOutcome.TooManyMembers, refused.Outcome);
        Assert.Contains("daily run cap", refused.Error);

        var accepted = await fixture.Orchestrator.StartAsync(
            StartRequest(fixture.Battery.Id, runsPerSuite: 2, allowCapWait: true), "user", ct);
        Assert.Equal(BenchmarkBatteryStartOutcome.Started, accepted.Outcome);

        await fixture.Orchestrator.CancelAsync(accepted.BatteryRunId!.Value, ct);
    }

    [Fact]
    public async Task Start_IsRefused_WhenTheCapIsFull_WithoutCapWait()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = await CreateFixtureAsync(CreateConfig(maxRunsPerDay: 10, maxRunsPerHour: 1));
        await FillCapAsync(fixture);

        var result = await fixture.Orchestrator.StartAsync(StartRequest(fixture.Battery.Id), "user", ct);

        Assert.Equal(BenchmarkBatteryStartOutcome.SpendDenied, result.Outcome);
        Assert.Null(fixture.RunManager.OrchestratorOwner);

        using var readback = CreateDbContext(fixture.DbName);
        Assert.Empty(await readback.BenchmarkBatteryRuns.ToListAsync(ct));
    }

    /// <summary>
    /// With cap wait, a full hourly or daily cap starts the battery run waiting on the cap, and the
    /// drive loop launches nothing until the window has room.
    /// </summary>
    [Theory]
    [InlineData(1, 10)]
    [InlineData(10, 1)]
    public async Task Start_WhenTheCapIsFull_WithCapWait_StartsWaitingForCap(int maxRunsPerHour, int maxRunsPerDay)
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = await CreateFixtureAsync(CreateConfig(maxRunsPerDay: maxRunsPerDay, maxRunsPerHour: maxRunsPerHour));
        await FillCapAsync(fixture);

        var result = await fixture.Orchestrator.StartAsync(
            StartRequest(fixture.Battery.Id, allowCapWait: true), "user", ct);
        Assert.Equal(BenchmarkBatteryStartOutcome.Started, result.Outcome);

        try
        {
            Assert.Equal(BenchmarkRunManager.BatteryOwner(result.BatteryRunId!.Value), fixture.RunManager.OrchestratorOwner);

            using var readback = CreateDbContext(fixture.DbName);
            var row = await readback.BenchmarkBatteryRuns.SingleAsync(b => b.Id == result.BatteryRunId.Value, ct);
            Assert.Equal(BenchmarkRunSeriesStatus.WaitingForCap, row.Status);
            Assert.Equal(1, await readback.BenchmarkRuns.CountAsync(ct));
        }
        finally
        {
            await fixture.Orchestrator.CancelAsync(result.BatteryRunId!.Value, ct);
        }
    }

    /// <summary>A start that waits on the cap still validates every suite before anything is written.</summary>
    [Fact]
    public async Task Start_WhenTheCapIsFull_WithCapWait_StillValidatesEverySuite()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = await CreateFixtureAsync(CreateConfig(maxRunsPerDay: 10, maxRunsPerHour: 1), suiteBAssessed: false);
        await FillCapAsync(fixture);

        var result = await fixture.Orchestrator.StartAsync(
            StartRequest(fixture.Battery.Id, allowCapWait: true), "user", ct);

        Assert.Equal(BenchmarkBatteryStartOutcome.Invalid, result.Outcome);
        Assert.Equal(1, result.SuiteIndex);
        Assert.Contains("assessed difficulty", result.Error);
        Assert.Null(fixture.RunManager.OrchestratorOwner);

        using var readback = CreateDbContext(fixture.DbName);
        Assert.Empty(await readback.BenchmarkBatteryRuns.ToListAsync(ct));
    }

    [Fact]
    public async Task Start_IsRefused_OnASpendDenialThatIsNotACap_EvenWithCapWait()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = await CreateFixtureAsync(denySpendForAnotherReason: true);

        var result = await fixture.Orchestrator.StartAsync(
            StartRequest(fixture.Battery.Id, allowCapWait: true), "user", ct);

        Assert.Equal(BenchmarkBatteryStartOutcome.SpendDenied, result.Outcome);
        Assert.Equal(OtherDenialGuard.Reason, result.Error);
        Assert.Null(fixture.RunManager.OrchestratorOwner);

        using var readback = CreateDbContext(fixture.DbName);
        Assert.Empty(await readback.BenchmarkBatteryRuns.ToListAsync(ct));
    }

    [Fact]
    public async Task SpendCheck_NamesTheCapThatRefused()
    {
        var ct = TestContext.Current.CancellationToken;

        var hourly = await CreateFixtureAsync(CreateConfig(maxRunsPerDay: 10, maxRunsPerHour: 1));
        await FillCapAsync(hourly);
        using (var db = CreateDbContext(hourly.DbName))
        {
            var guard = new BenchmarkComplianceGuard(CreateConfig(maxRunsPerDay: 10, maxRunsPerHour: 1), db);
            var check = await guard.CheckSpendAsync(ct: ct);
            Assert.False(check.Allowed);
            Assert.Equal(BenchmarkSpendDenialKind.HourlyCap, check.DenialKind);
            Assert.True(check.IsCapDenial);
            Assert.Equal((check.Allowed, check.DenialReason), await guard.CanSpendAsync(ct: ct));
        }

        var daily = await CreateFixtureAsync(CreateConfig(maxRunsPerDay: 1, maxRunsPerHour: 10));
        await FillCapAsync(daily);
        using (var db = CreateDbContext(daily.DbName))
        {
            var check = await new BenchmarkComplianceGuard(CreateConfig(maxRunsPerDay: 1, maxRunsPerHour: 10), db).CheckSpendAsync(ct: ct);
            Assert.Equal(BenchmarkSpendDenialKind.DailyCap, check.DenialKind);
            Assert.True(check.IsCapDenial);
        }

        using (var db = CreateDbContext(hourly.DbName))
        {
            var check = await new BenchmarkComplianceGuard(CreateConfig(), db).CheckSpendAsync(ct: ct);
            Assert.True(check.Allowed);
            Assert.Equal(BenchmarkSpendDenialKind.None, check.DenialKind);
            Assert.False(check.IsCapDenial);
        }

        Assert.False(BenchmarkSpendCheck.Deny(BenchmarkSpendDenialKind.Other, "no").IsCapDenial);
    }

    /// <summary>The code fallbacks of the run caps and the battery member cap, with no configuration.</summary>
    [Fact]
    public void Caps_FallBackTo120RunsPerDay_30PerHour_And120BatteryMembers()
    {
        var guard = new BenchmarkComplianceGuard(new ConfigurationBuilder().Build(), null!);

        Assert.Equal(120, guard.MaxRunsPerDay);
        Assert.Equal(30, guard.MaxRunsPerHour);
        Assert.Equal(120, guard.MaxBatteryMembers);
    }

    /// <summary>
    /// Every suite's request is validated before anything is written, so a suite the launcher would
    /// refuse — here one without assessed difficulties — fails the start, by name, before any spend.
    /// </summary>
    [Fact]
    public async Task Start_ValidatesEverySuite_AndNamesTheOneThatFails()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = await CreateFixtureAsync(suiteBAssessed: false);

        var result = await fixture.Orchestrator.StartAsync(StartRequest(fixture.Battery.Id), "user", ct);

        Assert.Equal(BenchmarkBatteryStartOutcome.Invalid, result.Outcome);
        Assert.Equal(1, result.SuiteIndex);
        Assert.Contains("Suite 'Suite B'", result.Error);
        Assert.Contains("assessed difficulty", result.Error);

        using var readback = CreateDbContext(fixture.DbName);
        Assert.Empty(await readback.BenchmarkBatteryRuns.ToListAsync(ct));
        Assert.Empty(await readback.BenchmarkRuns.ToListAsync(ct));
    }

    [Fact]
    public async Task Start_WithAnUnacknowledgedSameProviderPair_AsksForTheAcknowledgment()
    {
        var fixture = await CreateFixtureAsync();
        var request = StartRequest(fixture.Battery.Id);
        request.Run.AssessorModelConfigurationId = 1;

        var result = await fixture.Orchestrator.StartAsync(request, "user", TestContext.Current.CancellationToken);

        Assert.Equal(BenchmarkBatteryStartOutcome.SameProviderNotAcknowledged, result.Outcome);
        Assert.NotNull(result.SameProviderWarning);
    }

    /// <summary>
    /// A claim that outlived a refused start would block every run until the service restarts, so
    /// every refusal path returns with no claim held.
    /// </summary>
    [Fact]
    public async Task RefusedStartsAndResumes_LeaveNoClaimBehind()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = await CreateFixtureAsync(CreateConfig(maxRunsPerDay: 3, maxRunsPerHour: 3, maxMembers: 3), suiteBAssessed: false);
        var orchestrator = fixture.Orchestrator;

        var withAttach = StartRequest(fixture.Battery.Id);
        withAttach.Attach = new List<BenchmarkBatteryAttachDto> { new() { SuiteIndex = 0, Round = 1, RunId = 1 } };
        var sameProvider = StartRequest(fixture.Battery.Id);
        sameProvider.Run.AssessorModelConfigurationId = 1;

        Assert.False((await orchestrator.StartAsync(StartRequest(4242), "user", ct)).Started);
        Assert.False((await orchestrator.StartAsync(StartRequest(fixture.Battery.Id, runsPerSuite: 0), "user", ct)).Started);
        Assert.False((await orchestrator.StartAsync(StartRequest(fixture.Battery.Id, runsPerSuite: 2, allowCapWait: true), "user", ct)).Started);
        Assert.False((await orchestrator.StartAsync(withAttach, "user", ct)).Started);
        Assert.False((await orchestrator.StartAsync(sameProvider, "user", ct)).Started);
        Assert.False((await orchestrator.StartAsync(StartRequest(fixture.Battery.Id), "user", ct)).Started);

        var cancelled = await SeedBatteryRunAsync(fixture, BenchmarkRunSeriesStatus.Cancelled);
        var guarded = await SeedBatteryRunAsync(fixture, BenchmarkRunSeriesStatus.Stopped);
        await SeedMemberAsync(fixture, guarded, suiteIndex: 0, guardFailure: "moved");

        Assert.False((await orchestrator.ResumeAsync(cancelled.Id, BenchmarkBatteryResumeMode.Continue, ct)).Started);
        Assert.False((await orchestrator.ResumeAsync(guarded.Id, BenchmarkBatteryResumeMode.Continue, ct)).Started);
        Assert.False((await orchestrator.ResumeAsync(4242, BenchmarkBatteryResumeMode.Continue, ct)).Started);

        Assert.Null(fixture.RunManager.OrchestratorOwner);
    }

    // --- Start ---------------------------------------------------------------------------------

    [Fact]
    public async Task Start_RecordsTheDefinitionTheResolvedRequestAndTheFingerprints()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = await CreateFixtureAsync();
        fixture.Orchestrator.Fingerprint = suiteId =>
            new BenchmarkInstrumentFingerprint("prompt-" + suiteId, "guides", "kb", "wiki", "source");

        var request = StartRequest(fixture.Battery.Id, runsPerSuite: 2, allowCapWait: true);
        request.Run.RunCount = 7;

        var result = await fixture.Orchestrator.StartAsync(request, "user", ct);
        Assert.True(result.Started);

        try
        {
            using var readback = CreateDbContext(fixture.DbName);
            var batteryRun = await readback.BenchmarkBatteryRuns.SingleAsync(b => b.Id == result.BatteryRunId!.Value, ct);

            Assert.Equal(fixture.Battery.Id, batteryRun.BenchmarkBatteryId);
            Assert.Equal(fixture.Battery.DefinitionSha256, batteryRun.DefinitionSha256);
            Assert.Equal(2, batteryRun.RunsPerSuite);
            Assert.Equal(4, batteryRun.RequestedMemberCount);
            Assert.True(batteryRun.AllowCapWait);

            var definition = BenchmarkBatteryDefinition.FromJson(batteryRun.DefinitionJson);
            Assert.Equal(new[] { fixture.SuiteA.Id, fixture.SuiteB.Id }, definition.Suites.Select(s => s.SuiteId));

            // Stored resolved: one run per launch, the battery's cap preference, references disallowed.
            var stored = BenchmarkBatteryOrchestrator.DeserializeRequest(batteryRun);
            Assert.NotNull(stored);
            Assert.Equal(1, stored!.RunCount);
            Assert.True(stored.AllowCapWait);
            Assert.False(stored.AllowSourceCodeReferences);
            Assert.Equal(1, stored.TestedModelConfigurationId);

            var fingerprints = BenchmarkBatteryOrchestrator.ReadFingerprints(batteryRun.SuiteFingerprintsJson);
            Assert.Equal("prompt-" + fixture.SuiteA.Id, fingerprints[0].CandidateSystemPromptSha256);
            Assert.Equal("prompt-" + fixture.SuiteB.Id, fingerprints[1].CandidateSystemPromptSha256);
        }
        finally
        {
            await fixture.Orchestrator.CancelAsync(result.BatteryRunId!.Value, ct);
        }
    }

    // --- Report writer ---------------------------------------------------------------------------

    /// <summary>
    /// The writer is the battery run's: stored on the row, and absent from the request every member
    /// is launched from, so no member writes documents of its own.
    /// </summary>
    [Fact]
    public async Task Start_StoresTheReportWriterOnTheBatteryRun_AndLaunchesMembersWithoutOne()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = await CreateFixtureAsync();
        await AddConfigurationAsync(fixture, 3, "Google", "gemini-3-pro", "Writer Model");

        var request = StartRequest(fixture.Battery.Id);
        request.Run.ReportWriterModelConfigurationId = 3;

        var result = await fixture.Orchestrator.StartAsync(request, "user", ct);
        Assert.Equal(BenchmarkBatteryStartOutcome.Started, result.Outcome);

        try
        {
            using var readback = CreateDbContext(fixture.DbName);
            var row = await readback.BenchmarkBatteryRuns.SingleAsync(b => b.Id == result.BatteryRunId!.Value, ct);

            Assert.Equal(3, row.ReportWriterModelConfigurationId);
            Assert.Equal(BenchmarkRunReportDocumentsStatus.NotRequested, row.ReportDocumentsStatus);
            Assert.Null(row.ReportDocumentsMessage);

            var raw = JsonSerializer.Deserialize<StartBenchmarkRunRequest>(row.StartRequestJson);
            Assert.Null(raw!.ReportWriterModelConfigurationId);
            Assert.Null(BenchmarkBatteryOrchestrator.RequestForSuite(row, fixture.SuiteB.Id)!.ReportWriterModelConfigurationId);
        }
        finally
        {
            await fixture.Orchestrator.CancelAsync(result.BatteryRunId!.Value, ct);
        }
    }

    [Fact]
    public async Task Start_WithoutAReportWriter_StoresNone()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = await CreateFixtureAsync(CreateConfig(maxRunsPerDay: 10, maxRunsPerHour: 1));
        await FillCapAsync(fixture);

        var result = await fixture.Orchestrator.StartAsync(StartRequest(fixture.Battery.Id, allowCapWait: true), "user", ct);
        Assert.True(result.Started);

        try
        {
            using var readback = CreateDbContext(fixture.DbName);
            var row = await readback.BenchmarkBatteryRuns.SingleAsync(b => b.Id == result.BatteryRunId!.Value, ct);
            Assert.Null(row.ReportWriterModelConfigurationId);
            Assert.Equal(BenchmarkRunReportDocumentsStatus.NotRequested, row.ReportDocumentsStatus);
        }
        finally
        {
            await fixture.Orchestrator.CancelAsync(result.BatteryRunId!.Value, ct);
        }
    }

    /// <summary>A battery run stored before it held its own writer still launches its members without one.</summary>
    [Fact]
    public void StoredRequest_CarryingAWriter_IsReadWithoutIt()
    {
        var batteryRun = new BenchmarkBatteryRun
        {
            StartRequestJson = "{\"SuiteId\":1,\"TestedModelConfigurationId\":1,\"AssessorModelConfigurationId\":2," +
                               "\"ReportWriterModelConfigurationId\":3,\"AcknowledgeSameProviderReportWriter\":true}"
        };

        var forSuite = BenchmarkBatteryOrchestrator.RequestForSuite(batteryRun, 10);

        Assert.NotNull(forSuite);
        Assert.Null(forSuite!.ReportWriterModelConfigurationId);
        Assert.False(forSuite.AcknowledgeSameProviderReportWriter);
    }

    [Fact]
    public async Task Start_WithTheModelUnderTestAsWriter_IsInvalid_AndWritesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = await CreateFixtureAsync();

        var request = StartRequest(fixture.Battery.Id);
        request.Run.ReportWriterModelConfigurationId = 1;

        var result = await fixture.Orchestrator.StartAsync(request, "user", ct);

        Assert.Equal(BenchmarkBatteryStartOutcome.Invalid, result.Outcome);
        Assert.Equal(BenchmarkRunReportDocumentService.ModelUnderTestMessage, result.Error);
        Assert.Null(fixture.RunManager.OrchestratorOwner);

        using var readback = CreateDbContext(fixture.DbName);
        Assert.Empty(await readback.BenchmarkBatteryRuns.ToListAsync(ct));
    }

    [Fact]
    public async Task Start_WithAnUnusableWriter_IsInvalid()
    {
        var fixture = await CreateFixtureAsync();

        var request = StartRequest(fixture.Battery.Id);
        request.Run.ReportWriterModelConfigurationId = 4242;

        var result = await fixture.Orchestrator.StartAsync(request, "user", TestContext.Current.CancellationToken);

        Assert.Equal(BenchmarkBatteryStartOutcome.Invalid, result.Outcome);
        Assert.Equal(BenchmarkRunReportDocumentService.InvalidWriterMessage, result.Error);
    }

    /// <summary>
    /// A writer of the candidate's provider is asked about once, at battery start, with the warning's
    /// role naming the writer; the acknowledgment on the run settings lets the start through.
    /// </summary>
    [Fact]
    public async Task Start_WithAWriterOfTheCandidatesProvider_AsksForTheAcknowledgment_ThenStarts()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = await CreateFixtureAsync();
        await AddConfigurationAsync(fixture, 3, "Anthropic", "claude-sonnet-5", "Writer Model");

        var request = StartRequest(fixture.Battery.Id);
        request.Run.ReportWriterModelConfigurationId = 3;

        var asked = await fixture.Orchestrator.StartAsync(request, "user", ct);

        Assert.Equal(BenchmarkBatteryStartOutcome.SameProviderNotAcknowledged, asked.Outcome);
        Assert.Equal("reportWriter", asked.SameProviderWarning!.Role);
        Assert.Null(asked.SuiteIndex);
        Assert.Null(fixture.RunManager.OrchestratorOwner);
        using (var readback = CreateDbContext(fixture.DbName))
        {
            Assert.Empty(await readback.BenchmarkBatteryRuns.ToListAsync(ct));
        }

        request.Run.AcknowledgeSameProviderReportWriter = true;
        var started = await fixture.Orchestrator.StartAsync(request, "user", ct);

        Assert.Equal(BenchmarkBatteryStartOutcome.Started, started.Outcome);
        await fixture.Orchestrator.CancelAsync(started.BatteryRunId!.Value, ct);
    }

    // --- Attaching existing runs ------------------------------------------------------------------

    /// <summary>
    /// A finished run of one of the fixture's suites by the battery's tested configuration
    /// (<see cref="RunRequest"/>'s id 1), belonging to no battery run.
    /// </summary>
    private static async Task<BenchmarkRun> SeedRunAsync(
        Fixture fixture,
        int suiteIndex,
        int hoursAgo,
        int? qualityIndex = 70,
        string? toolGuides = null)
    {
        using var db = CreateDbContext(fixture.DbName);
        var suite = suiteIndex == 0 ? fixture.SuiteA : fixture.SuiteB;

        var run = new BenchmarkRun
        {
            BenchmarkSuiteId = suite.Id,
            BenchmarkSuiteIdUsed = suite.Id,
            SuiteName = suite.Name,
            TestedModelConfigurationId = 1,
            TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Anthropic", modelId: "claude-opus-5", displayName: "claude-opus-5"),
            AssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "OpenAI", modelId: "gpt-5", displayName: "Assessor"),
            Status = qualityIndex.HasValue ? BenchmarkRunStatus.Completed : BenchmarkRunStatus.CompletedWithErrors,
            QualityIndex = qualityIndex,
            TerminalFailureAnswerCount = qualityIndex.HasValue ? 0 : 1,
            ScoringMethodVersion = BenchmarkAssessmentPrompt.ScoringMethodVersion,
            HarnessVersion = BenchmarkAssessmentPrompt.HarnessVersion,
            ToolGuidesSha256 = toolGuides,
            StartedAtUtc = DateTime.UtcNow.AddHours(-hoursAgo),
            CompletedAtUtc = DateTime.UtcNow.AddHours(-hoursAgo).AddMinutes(20)
        };

        // One answer to the suite's question gives the run an exam identity, so two runs of a suite pool.
        var question = suite.Questions.Single();
        run.Answers.Add(new BenchmarkRunAnswer
        {
            BenchmarkQuestionId = question.Id,
            BenchmarkQuestionIdUsed = question.Id,
            ItemRevisionUsed = 1,
            AssessedDifficulty = question.AssessedDifficulty,
            OrderIndex = 1,
            QuestionText = question.QuestionText,
            AnswerText = "Answer",
            Status = BenchmarkAnswerStatus.Ok,
            QualityScore = qualityIndex ?? 0
        });
        db.BenchmarkRuns.Add(run);
        await db.SaveChangesAsync();
        return run;
    }

    /// <summary>
    /// Every slot attached: the battery run is finished at start, its analysis attempted, and nothing
    /// is launched and no claim taken.
    /// </summary>
    [Fact]
    public async Task Start_EntirelyFromAttachedRuns_CompletesWithoutALaunch()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = await CreateFixtureAsync();
        var runA = await SeedRunAsync(fixture, suiteIndex: 0, hoursAgo: 5);
        var runB = await SeedRunAsync(fixture, suiteIndex: 1, hoursAgo: 4);

        var request = StartRequest(fixture.Battery.Id);
        request.Attach = new List<BenchmarkBatteryAttachDto>
        {
            new() { SuiteIndex = 0, Round = 1, RunId = runA.Id },
            new() { SuiteIndex = 1, Round = 1, RunId = runB.Id }
        };

        var result = await fixture.Orchestrator.StartAsync(request, "user", ct);

        Assert.Equal(BenchmarkBatteryStartOutcome.Started, result.Outcome);
        Assert.False(fixture.Orchestrator.IsDriving(result.BatteryRunId!.Value));
        Assert.Null(fixture.RunManager.OrchestratorOwner);

        using var readback = CreateDbContext(fixture.DbName);
        var row = await readback.BenchmarkBatteryRuns.SingleAsync(b => b.Id == result.BatteryRunId.Value, ct);
        Assert.Equal(BenchmarkRunSeriesStatus.Completed, row.Status);
        Assert.Equal(2, row.CompletedMemberCount);
        Assert.NotNull(row.CompletedAtUtc);
        Assert.True(await readback.BenchmarkBatteryAnalyses.AnyAsync(a => a.BenchmarkBatteryRunId == row.Id, ct)
                    || (row.ErrorMessage ?? string.Empty).Contains("analysis"));

        var members = await readback.BenchmarkBatteryRunMembers.Where(m => m.BenchmarkBatteryRunId == row.Id).ToListAsync(ct);
        Assert.Equal(2, members.Count);
        Assert.All(members, m => Assert.Equal(BenchmarkBatteryMemberOrigin.Attached, m.Origin));
        Assert.Equal(2, await readback.BenchmarkRuns.CountAsync(ct));
    }

    /// <summary>
    /// The preview fills each slot in planner order with the newest run that qualifies against the
    /// runs chosen before it: an unusable newest run is passed over, and the one run of suite B fills
    /// round 1 only, so round 2 of suite B is launched and says why.
    /// </summary>
    [Fact]
    public async Task Preview_ChoosesTheNewestEligibleRunPerSlot_AndNeverOneRunForTwoSlots()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = await CreateFixtureAsync();
        var olderA = await SeedRunAsync(fixture, suiteIndex: 0, hoursAgo: 9);
        var newerA = await SeedRunAsync(fixture, suiteIndex: 0, hoursAgo: 6);
        var withheldA = await SeedRunAsync(fixture, suiteIndex: 0, hoursAgo: 1, qualityIndex: null);
        var onlyB = await SeedRunAsync(fixture, suiteIndex: 1, hoursAgo: 3);

        var result = await fixture.Orchestrator.PreviewReuseAsync(StartRequest(fixture.Battery.Id, runsPerSuite: 2), ct);

        Assert.Equal(BenchmarkBatteryAttachOutcome.Ok, result.Outcome);
        var preview = result.Preview!;
        Assert.Equal(new[] { "0/1", "1/1", "0/2", "1/2" }, preview.Slots.Select(s => $"{s.SuiteIndex}/{s.Round}"));
        Assert.Equal(new long?[] { newerA.Id, onlyB.Id, olderA.Id, null }, preview.Slots.Select(s => s.RunId));
        Assert.Contains("already fills another slot", preview.Slots[3].Reason);
        Assert.Null(preview.Slots[0].Reason);
        Assert.DoesNotContain(preview.Attach, a => a.RunId == withheldA.Id);
        Assert.Equal(3, preview.ReusedCount);
        Assert.Equal(1, preview.LaunchCount);
        Assert.Equal(3, preview.Attach.Select(a => a.RunId).Distinct().Count());

        using var readback = CreateDbContext(fixture.DbName);
        Assert.Empty(await readback.BenchmarkBatteryRuns.ToListAsync(ct));
        Assert.Null(fixture.RunManager.OrchestratorOwner);
    }

    [Fact]
    public async Task Preview_WithNoEarlierRun_NamesWhyEachSlotIsLaunched()
    {
        var fixture = await CreateFixtureAsync();

        var result = await fixture.Orchestrator.PreviewReuseAsync(StartRequest(fixture.Battery.Id), TestContext.Current.CancellationToken);

        Assert.Equal(0, result.Preview!.ReusedCount);
        Assert.Equal(2, result.Preview.LaunchCount);
        Assert.All(result.Preview.Slots, s => Assert.Contains("No earlier run", s.Reason));
    }

    [Fact]
    public async Task Preview_OfAnUnknownBattery_IsNotFound()
    {
        var fixture = await CreateFixtureAsync();

        var result = await fixture.Orchestrator.PreviewReuseAsync(StartRequest(4242), TestContext.Current.CancellationToken);

        Assert.Equal(BenchmarkBatteryAttachOutcome.NotFound, result.Outcome);
    }

    /// <summary>
    /// The start judges the previewed runs again against the fingerprints it records: a run that
    /// qualified in the preview but no longer does refuses the whole start, naming it.
    /// </summary>
    [Fact]
    public async Task Start_IsRefused_WhenAPreviewedRunStoppedQualifying()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = await CreateFixtureAsync();
        var runA = await SeedRunAsync(fixture, suiteIndex: 0, hoursAgo: 5, toolGuides: "guides-then");
        fixture.Orchestrator.Fingerprint = _ => new BenchmarkInstrumentFingerprint(null, "guides-then", null, null, null);

        var preview = (await fixture.Orchestrator.PreviewReuseAsync(StartRequest(fixture.Battery.Id), ct)).Preview!;
        Assert.Equal(runA.Id, preview.Slots[0].RunId);

        fixture.Orchestrator.Fingerprint = _ => new BenchmarkInstrumentFingerprint(null, "guides-now", null, null, null);
        var request = StartRequest(fixture.Battery.Id);
        request.Attach = preview.Attach;

        var result = await fixture.Orchestrator.StartAsync(request, "user", ct);

        Assert.Equal(BenchmarkBatteryStartOutcome.Invalid, result.Outcome);
        Assert.Contains($"Run #{runA.Id}", result.Error);
        Assert.Contains("no longer qualifies", result.Error);
        Assert.Contains("ToolGuidesSha256", result.Error);
        Assert.Null(fixture.RunManager.OrchestratorOwner);

        using var readback = CreateDbContext(fixture.DbName);
        Assert.Empty(await readback.BenchmarkBatteryRuns.ToListAsync(ct));
    }

    /// <summary>
    /// Attaching to a slot whose member's index was withheld supersedes that member; with every slot
    /// then usable, the stopped battery run is finished as the drive loop would finish it.
    /// </summary>
    [Fact]
    public async Task Attach_SupersedesAnUnusableMember_AndFinishesTheBatteryRun()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = await CreateFixtureAsync();
        var batteryRun = await SeedBatteryRunAsync(fixture, BenchmarkRunSeriesStatus.Stopped);
        await SeedMemberAsync(fixture, batteryRun, suiteIndex: 0);
        var withheld = await SeedMemberAsync(fixture, batteryRun, suiteIndex: 1,
            status: BenchmarkRunStatus.CompletedWithErrors, qualityIndex: null, terminalFailures: 1);
        var replacement = await SeedRunAsync(fixture, suiteIndex: 1, hoursAgo: 2);

        var result = await fixture.Orchestrator.AttachAsync(batteryRun.Id, suiteIndex: 1, round: 1, replacement.Id, ct);

        Assert.Equal(BenchmarkBatteryAttachOutcome.Ok, result.Outcome);

        using var readback = CreateDbContext(fixture.DbName);
        Assert.True((await readback.BenchmarkBatteryRunMembers.SingleAsync(m => m.Id == withheld.Id, ct)).Superseded);
        var attached = await readback.BenchmarkBatteryRunMembers.SingleAsync(
            m => m.BenchmarkBatteryRunId == batteryRun.Id && m.BenchmarkRunId == replacement.Id, ct);
        Assert.Equal(BenchmarkBatteryMemberOrigin.Attached, attached.Origin);
        Assert.False(attached.Superseded);
        Assert.Equal(1, attached.SuiteIndex);
        Assert.Equal(1, attached.Round);

        var row = await readback.BenchmarkBatteryRuns.SingleAsync(b => b.Id == batteryRun.Id, ct);
        Assert.Equal(2, row.CompletedMemberCount);
        Assert.Equal(BenchmarkRunSeriesStatus.Completed, row.Status);
        Assert.Null(row.StopReason);
    }

    [Fact]
    public async Task Attach_ToASlotWithAUsableMember_IsIneligible()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = await CreateFixtureAsync();
        var batteryRun = await SeedBatteryRunAsync(fixture, BenchmarkRunSeriesStatus.Stopped);
        await SeedMemberAsync(fixture, batteryRun, suiteIndex: 0);
        var other = await SeedRunAsync(fixture, suiteIndex: 0, hoursAgo: 2);

        var result = await fixture.Orchestrator.AttachAsync(batteryRun.Id, suiteIndex: 0, round: 1, other.Id, ct);

        Assert.Equal(BenchmarkBatteryAttachOutcome.Ineligible, result.Outcome);
        Assert.Contains("already holds a usable member", result.Error);
    }

    [Theory]
    [InlineData(BenchmarkRunSeriesStatus.Running)]
    [InlineData(BenchmarkRunSeriesStatus.WaitingForCap)]
    [InlineData(BenchmarkRunSeriesStatus.Cancelled)]
    [InlineData(BenchmarkRunSeriesStatus.Failed)]
    public async Task Attach_IsAConflict_InAStateThatTakesNoRun(BenchmarkRunSeriesStatus status)
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = await CreateFixtureAsync();
        var batteryRun = await SeedBatteryRunAsync(fixture, status);
        var run = await SeedRunAsync(fixture, suiteIndex: 0, hoursAgo: 2);

        var result = await fixture.Orchestrator.AttachAsync(batteryRun.Id, suiteIndex: 0, round: 1, run.Id, ct);

        Assert.Equal(BenchmarkBatteryAttachOutcome.Conflict, result.Outcome);
    }

    [Fact]
    public async Task Attach_WhileTheDriveLoopHoldsTheClaim_IsAConflict()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = await CreateFixtureAsync();
        var batteryRun = await SeedBatteryRunAsync(fixture, BenchmarkRunSeriesStatus.Stopped);
        var run = await SeedRunAsync(fixture, suiteIndex: 0, hoursAgo: 2);
        Assert.True(fixture.RunManager.TryClaimOrchestrator(BenchmarkRunManager.BatteryOwner(batteryRun.Id)));

        var result = await fixture.Orchestrator.AttachAsync(batteryRun.Id, suiteIndex: 0, round: 1, run.Id, ct);

        Assert.Equal(BenchmarkBatteryAttachOutcome.Conflict, result.Outcome);
    }

    [Fact]
    public async Task Candidates_ListTheSuitesRunsNewestFirst_WithEligibilityAndReasons()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = await CreateFixtureAsync();
        var batteryRun = await SeedBatteryRunAsync(fixture, BenchmarkRunSeriesStatus.Stopped);
        var older = await SeedRunAsync(fixture, suiteIndex: 1, hoursAgo: 8);
        var withheld = await SeedRunAsync(fixture, suiteIndex: 1, hoursAgo: 2, qualityIndex: null);
        await SeedRunAsync(fixture, suiteIndex: 0, hoursAgo: 1);

        var result = await fixture.Orchestrator.GetAttachCandidatesAsync(batteryRun.Id, suiteIndex: 1, round: 1, ct);

        Assert.Equal(BenchmarkBatteryAttachOutcome.Ok, result.Outcome);
        var candidates = result.Candidates!;
        Assert.Equal(new[] { withheld.Id, older.Id }, candidates.Select(c => c.RunId));
        Assert.False(candidates[0].Eligible);
        Assert.Contains(BenchmarkBatteryPlanner.IndexWithheldProviderFailureReason, candidates[0].Reason);
        Assert.True(candidates[1].Eligible);
        Assert.Null(candidates[1].Reason);
    }

    // --- Resume --------------------------------------------------------------------------------

    [Theory]
    [InlineData(BenchmarkRunSeriesStatus.Cancelled)]
    [InlineData(BenchmarkRunSeriesStatus.Completed)]
    [InlineData(BenchmarkRunSeriesStatus.Failed)]
    public async Task Resume_IsRefused_ForATerminalBatteryRun(BenchmarkRunSeriesStatus status)
    {
        var fixture = await CreateFixtureAsync();
        var batteryRun = await SeedBatteryRunAsync(fixture, status);

        var result = await fixture.Orchestrator.ResumeAsync(batteryRun.Id, BenchmarkBatteryResumeMode.Continue, TestContext.Current.CancellationToken);

        Assert.Equal(BenchmarkBatteryStartOutcome.Invalid, result.Outcome);
        Assert.Contains("cannot be resumed", result.Error);
    }

    [Fact]
    public async Task Resume_OfAnUnknownBatteryRun_IsNotFound()
    {
        var fixture = await CreateFixtureAsync();

        var result = await fixture.Orchestrator.ResumeAsync(4242, BenchmarkBatteryResumeMode.Continue, TestContext.Current.CancellationToken);

        Assert.Equal(BenchmarkBatteryStartOutcome.NotFound, result.Outcome);
    }

    /// <summary>A battery run started with cap wait resumes into a full hourly or daily cap waiting on it.</summary>
    [Theory]
    [InlineData(1, 10)]
    [InlineData(10, 1)]
    public async Task Resume_WhenTheCapIsFull_WithCapWait_ResumesWaitingForCap(int maxRunsPerHour, int maxRunsPerDay)
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = await CreateFixtureAsync(CreateConfig(maxRunsPerDay: maxRunsPerDay, maxRunsPerHour: maxRunsPerHour));
        var batteryRun = await SeedBatteryRunAsync(fixture, BenchmarkRunSeriesStatus.Stopped, allowCapWait: true);
        await FillCapAsync(fixture);

        var result = await fixture.Orchestrator.ResumeAsync(batteryRun.Id, BenchmarkBatteryResumeMode.Continue, ct);
        Assert.Equal(BenchmarkBatteryStartOutcome.Started, result.Outcome);

        try
        {
            using var readback = CreateDbContext(fixture.DbName);
            var row = await readback.BenchmarkBatteryRuns.SingleAsync(b => b.Id == batteryRun.Id, ct);
            Assert.Equal(BenchmarkRunSeriesStatus.WaitingForCap, row.Status);
            Assert.Null(row.StopReason);
            Assert.Null(row.ErrorMessage);
            Assert.Equal(1, await readback.BenchmarkRuns.CountAsync(ct));
        }
        finally
        {
            await fixture.Orchestrator.CancelAsync(batteryRun.Id, ct);
        }
    }

    [Fact]
    public async Task Resume_WhenTheCapIsFull_WithoutCapWait_IsSpendDenied()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = await CreateFixtureAsync(CreateConfig(maxRunsPerDay: 10, maxRunsPerHour: 1));
        var batteryRun = await SeedBatteryRunAsync(fixture, BenchmarkRunSeriesStatus.Stopped);
        await FillCapAsync(fixture);

        var result = await fixture.Orchestrator.ResumeAsync(batteryRun.Id, BenchmarkBatteryResumeMode.Continue, ct);

        Assert.Equal(BenchmarkBatteryStartOutcome.SpendDenied, result.Outcome);
        Assert.False(fixture.Orchestrator.IsDriving(batteryRun.Id));
        Assert.Null(fixture.RunManager.OrchestratorOwner);

        using var readback = CreateDbContext(fixture.DbName);
        Assert.Equal(BenchmarkRunSeriesStatus.Stopped, (await readback.BenchmarkBatteryRuns.SingleAsync(b => b.Id == batteryRun.Id, ct)).Status);
    }

    [Fact]
    public async Task Resume_IsRefused_OnASpendDenialThatIsNotACap_EvenWithCapWait()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = await CreateFixtureAsync(denySpendForAnotherReason: true);
        var batteryRun = await SeedBatteryRunAsync(fixture, BenchmarkRunSeriesStatus.Stopped, allowCapWait: true);

        var result = await fixture.Orchestrator.ResumeAsync(batteryRun.Id, BenchmarkBatteryResumeMode.Continue, ct);

        Assert.Equal(BenchmarkBatteryStartOutcome.SpendDenied, result.Outcome);
        Assert.Equal(OtherDenialGuard.Reason, result.Error);
        Assert.Null(fixture.RunManager.OrchestratorOwner);
    }

    /// <summary>
    /// A kept member graded under another scoring method is an instrument change, so the refusal
    /// carries the moved key and the client offers Re-run under current instrument.
    /// </summary>
    [Fact]
    public async Task Resume_WhenAKeptMemberWasGradedUnderAnotherScoringMethod_IsAnInstrumentChange()
    {
        var fixture = await CreateFixtureAsync();
        var batteryRun = await SeedBatteryRunAsync(fixture, BenchmarkRunSeriesStatus.Stopped);
        await SeedMemberAsync(fixture, batteryRun, suiteIndex: 0, scoringMethod: 10);

        var result = await fixture.Orchestrator.ResumeAsync(batteryRun.Id, BenchmarkBatteryResumeMode.Continue, TestContext.Current.CancellationToken);

        Assert.Equal(BenchmarkBatteryStartOutcome.InstrumentChanged, result.Outcome);
        Assert.Equal(new[] { "ScoringMethodVersion" }, result.ChangedInstrumentHashes);
        Assert.Equal(batteryRun.Id, result.BatteryRunId);
        Assert.Contains("scoring method 10", result.Error);
        Assert.False(fixture.Orchestrator.IsDriving(batteryRun.Id));
        Assert.Null(fixture.RunManager.OrchestratorOwner);
    }

    [Fact]
    public async Task Continue_IsRefused_WhenTheHarnessVersionChanged()
    {
        var fixture = await CreateFixtureAsync();
        var batteryRun = await SeedBatteryRunAsync(fixture, BenchmarkRunSeriesStatus.Stopped);
        await SeedMemberAsync(fixture, batteryRun, suiteIndex: 0, harnessVersion: "1");

        var result = await fixture.Orchestrator.ResumeAsync(batteryRun.Id, BenchmarkBatteryResumeMode.Continue, TestContext.Current.CancellationToken);

        Assert.Equal(BenchmarkBatteryStartOutcome.InstrumentChanged, result.Outcome);
        Assert.Contains("harness", result.Error);
    }

    [Fact]
    public async Task Continue_IsRefused_WhenAMemberCarriesAGuardFailure()
    {
        var fixture = await CreateFixtureAsync();
        var batteryRun = await SeedBatteryRunAsync(fixture, BenchmarkRunSeriesStatus.Stopped);
        await SeedMemberAsync(fixture, batteryRun, suiteIndex: 0);
        await SeedMemberAsync(fixture, batteryRun, suiteIndex: 1, guardFailure: "ToolGuidesSha256 moved.");

        var result = await fixture.Orchestrator.ResumeAsync(batteryRun.Id, BenchmarkBatteryResumeMode.Continue, TestContext.Current.CancellationToken);

        Assert.Equal(BenchmarkBatteryStartOutcome.InstrumentChanged, result.Outcome);
        Assert.Contains("ToolGuidesSha256 moved.", result.Error);
    }

    [Fact]
    public async Task Continue_IsRefused_WhenTheUsableMembersAlreadyRefuseTheComposite()
    {
        var fixture = await CreateFixtureAsync();
        var batteryRun = await SeedBatteryRunAsync(fixture, BenchmarkRunSeriesStatus.Stopped, runsPerSuite: 2);
        await SeedMemberAsync(fixture, batteryRun, suiteIndex: 0, modelId: "claude-opus-5");
        await SeedMemberAsync(fixture, batteryRun, suiteIndex: 1, modelId: "claude-sonnet-5");

        var result = await fixture.Orchestrator.ResumeAsync(batteryRun.Id, BenchmarkBatteryResumeMode.Continue, TestContext.Current.CancellationToken);

        Assert.Equal(BenchmarkBatteryStartOutcome.InstrumentChanged, result.Outcome);
        Assert.Contains("already refuse the composite", result.Error);
    }

    [Fact]
    public async Task Continue_IsRefused_WhenARemainingSuitesFingerprintMoved()
    {
        var fixture = await CreateFixtureAsync();
        string recorded = JsonSerializer.Serialize(
            new Dictionary<int, BenchmarkInstrumentFingerprint>
            {
                [1] = new(null, "guides-at-start", null, null, null)
            },
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var batteryRun = await SeedBatteryRunAsync(fixture, BenchmarkRunSeriesStatus.Stopped, fingerprintsJson: recorded);
        await SeedMemberAsync(fixture, batteryRun, suiteIndex: 0);

        fixture.Orchestrator.Fingerprint = _ => new BenchmarkInstrumentFingerprint(null, "guides-now", null, null, null);

        var result = await fixture.Orchestrator.ResumeAsync(batteryRun.Id, BenchmarkBatteryResumeMode.Continue, TestContext.Current.CancellationToken);

        Assert.Equal(BenchmarkBatteryStartOutcome.InstrumentChanged, result.Outcome);
        Assert.Equal(new[] { "ToolGuidesSha256 (suite 'Suite B')" }, result.ChangedInstrumentHashes);
    }

    /// <summary>
    /// Continue replaces a member whose index is still withheld, and keeps one the operator repaired
    /// in place meanwhile with Re-run Failed Questions (decision 7).
    /// </summary>
    [Fact]
    public async Task Continue_SupersedesAnUnusableMember_AndKeepsARepairedOne()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = await CreateFixtureAsync();
        var batteryRun = await SeedBatteryRunAsync(fixture, BenchmarkRunSeriesStatus.CompletedWithErrors);
        var repaired = await SeedMemberAsync(fixture, batteryRun, suiteIndex: 0,
            status: BenchmarkRunStatus.CompletedWithErrors, qualityIndex: 70, terminalFailures: 0);
        var withheld = await SeedMemberAsync(fixture, batteryRun, suiteIndex: 1,
            status: BenchmarkRunStatus.CompletedWithErrors, qualityIndex: null, terminalFailures: 1);

        var result = await fixture.Orchestrator.ResumeAsync(batteryRun.Id, BenchmarkBatteryResumeMode.Continue, ct);
        Assert.Equal(BenchmarkBatteryStartOutcome.Started, result.Outcome);

        try
        {
            using var readback = CreateDbContext(fixture.DbName);
            Assert.False((await readback.BenchmarkBatteryRunMembers.SingleAsync(m => m.Id == repaired.Id, ct)).Superseded);
            Assert.True((await readback.BenchmarkBatteryRunMembers.SingleAsync(m => m.Id == withheld.Id, ct)).Superseded);
            Assert.Equal(1, (await readback.BenchmarkBatteryRuns.SingleAsync(b => b.Id == batteryRun.Id, ct)).CompletedMemberCount);
        }
        finally
        {
            await fixture.Orchestrator.CancelAsync(batteryRun.Id, ct);
        }
    }

    /// <summary>
    /// Re-running under the current instrument supersedes every member, the guarded one included,
    /// forgets the auto-created groups and re-records the fingerprints.
    /// </summary>
    [Fact]
    public async Task Rerun_SupersedesEveryMember_AndReRecordsTheFingerprints()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = await CreateFixtureAsync();
        var batteryRun = await SeedBatteryRunAsync(fixture, BenchmarkRunSeriesStatus.Stopped);
        var usable = await SeedMemberAsync(fixture, batteryRun, suiteIndex: 0);
        var guarded = await SeedMemberAsync(fixture, batteryRun, suiteIndex: 1, guardFailure: "moved");

        fixture.Orchestrator.Fingerprint = _ => new BenchmarkInstrumentFingerprint("prompt-now", "guides-now", null, null, null);

        var result = await fixture.Orchestrator.ResumeAsync(batteryRun.Id, BenchmarkBatteryResumeMode.RerunUnderCurrentInstrument, ct);
        Assert.Equal(BenchmarkBatteryStartOutcome.Started, result.Outcome);

        try
        {
            using var readback = CreateDbContext(fixture.DbName);
            Assert.True((await readback.BenchmarkBatteryRunMembers.SingleAsync(m => m.Id == usable.Id, ct)).Superseded);
            Assert.True((await readback.BenchmarkBatteryRunMembers.SingleAsync(m => m.Id == guarded.Id, ct)).Superseded);

            var row = await readback.BenchmarkBatteryRuns.SingleAsync(b => b.Id == batteryRun.Id, ct);
            Assert.Null(row.AutoCreatedGroupIdsJson);
            var fingerprints = BenchmarkBatteryOrchestrator.ReadFingerprints(row.SuiteFingerprintsJson);
            Assert.Equal("guides-now", fingerprints[0].ToolGuidesSha256);
            Assert.Equal("guides-now", fingerprints[1].ToolGuidesSha256);
        }
        finally
        {
            await fixture.Orchestrator.CancelAsync(batteryRun.Id, ct);
        }
    }

    // --- Cancel and reconcile ------------------------------------------------------------------

    [Fact]
    public async Task Cancel_CancelsTheInFlightMemberThroughTheRunManager_AndIsNotResumable()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = await CreateFixtureAsync();
        var batteryRun = await SeedBatteryRunAsync(fixture, BenchmarkRunSeriesStatus.Running);
        var member = await SeedMemberAsync(fixture, batteryRun, suiteIndex: 0, status: BenchmarkRunStatus.Running, qualityIndex: null);

        var cts = new CancellationTokenSource();
        Assert.True(fixture.RunManager.TryStart(member.BenchmarkRunId, cts, out _));

        Assert.True(await fixture.Orchestrator.CancelAsync(batteryRun.Id, ct));

        Assert.True(cts.IsCancellationRequested);
        Assert.Null(fixture.RunManager.CurrentRunId);

        using var readback = CreateDbContext(fixture.DbName);
        var cancelled = await readback.BenchmarkBatteryRuns.SingleAsync(b => b.Id == batteryRun.Id, ct);
        Assert.Equal(BenchmarkRunSeriesStatus.Cancelled, cancelled.Status);
        Assert.Null(cancelled.StopReason);
        Assert.NotNull(cancelled.CompletedAtUtc);

        var resume = await fixture.Orchestrator.ResumeAsync(batteryRun.Id, BenchmarkBatteryResumeMode.Continue, ct);
        Assert.Equal(BenchmarkBatteryStartOutcome.Invalid, resume.Outcome);
    }

    [Fact]
    public async Task Cancel_OfAnUnknownBatteryRun_ReportsFailure()
    {
        var fixture = await CreateFixtureAsync();

        Assert.False(await fixture.Orchestrator.CancelAsync(4242, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Reconcile_MovesOrphanedBatteryRunsToStopped_AndLeavesFinishedOnesAlone()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = await CreateFixtureAsync();
        var running = await SeedBatteryRunAsync(fixture, BenchmarkRunSeriesStatus.Running);
        var waiting = await SeedBatteryRunAsync(fixture, BenchmarkRunSeriesStatus.WaitingForCap);
        var pending = await SeedBatteryRunAsync(fixture, BenchmarkRunSeriesStatus.Pending);
        var completed = await SeedBatteryRunAsync(fixture, BenchmarkRunSeriesStatus.Completed);
        var cancelled = await SeedBatteryRunAsync(fixture, BenchmarkRunSeriesStatus.Cancelled);

        using (var db = CreateDbContext(fixture.DbName))
        {
            Assert.Equal(3, await fixture.Orchestrator.ReconcileOrphanedAsync(db, ct));
        }

        using var readback = CreateDbContext(fixture.DbName);
        foreach (long id in new[] { running.Id, waiting.Id, pending.Id })
        {
            var row = await readback.BenchmarkBatteryRuns.SingleAsync(b => b.Id == id, ct);
            Assert.Equal(BenchmarkRunSeriesStatus.Stopped, row.Status);
            Assert.Equal(BenchmarkRunSeriesStopReason.MemberFailed, row.StopReason);
            Assert.Contains("restarted", row.ErrorMessage);
        }

        Assert.Equal(BenchmarkRunSeriesStatus.Completed, (await readback.BenchmarkBatteryRuns.SingleAsync(b => b.Id == completed.Id, ct)).Status);
        Assert.Equal(BenchmarkRunSeriesStatus.Cancelled, (await readback.BenchmarkBatteryRuns.SingleAsync(b => b.Id == cancelled.Id, ct)).Status);

        using var empty = CreateDbContext(fixture.DbName);
        Assert.Equal(0, await fixture.Orchestrator.ReconcileOrphanedAsync(empty, ct));
    }

    // --- Reconcile after a member change and finishing through Continue ---------------------------

    /// <summary>Sets a seeded battery run's status, error message and finish time, as a finished battery run holds them.</summary>
    private static async Task MarkFinishedAsync(
        Fixture fixture, long batteryRunId, BenchmarkRunSeriesStatus status, string? errorMessage, DateTime completedAtUtc)
    {
        using var db = CreateDbContext(fixture.DbName);
        var row = await db.BenchmarkBatteryRuns.SingleAsync(b => b.Id == batteryRunId);
        row.Status = status;
        row.StopReason = null;
        row.ErrorMessage = errorMessage;
        row.CompletedAtUtc = completedAtUtc;
        await db.SaveChangesAsync();
    }

    private static async Task<BenchmarkBatteryRun> ReadBatteryRunAsync(Fixture fixture, long batteryRunId)
    {
        using var db = CreateDbContext(fixture.DbName);
        return await db.BenchmarkBatteryRuns.AsNoTracking().SingleAsync(b => b.Id == batteryRunId);
    }

    /// <summary>Waits until no drive loop drives the battery run and no claim is held, then reads it.</summary>
    private static async Task<BenchmarkBatteryRun> WaitUntilDrivenToTheEndAsync(Fixture fixture, long batteryRunId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (fixture.Orchestrator.IsDriving(batteryRunId) || fixture.RunManager.OrchestratorOwner != null)
        {
            Assert.True(DateTime.UtcNow < deadline, "The drive loop did not finish in time.");
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        return await ReadBatteryRunAsync(fixture, batteryRunId);
    }

    /// <summary>
    /// A CompletedWithErrors battery run whose one index-withheld member a re-run has repaired finishes
    /// by itself. It becomes Completed with no error message, keeps the
    /// time it first finished, gets one analysis, and its battery-completion documents are written.
    /// </summary>
    [Fact]
    public async Task ReconcileAfterMemberChange_FinishesARepairedBatteryRun_KeepingItsFinishTime_AndWritesItsDocuments()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var h = await BatteryReportHarness.CreateAsync(analyse: false);
        const long repairedRunId = 2;
        DateTime firstFinished = (await h.BatteryRunAsync()).CompletedAtUtc!.Value;

        await h.UpdateBatteryRunAsync(b =>
        {
            b.Status = BenchmarkRunSeriesStatus.CompletedWithErrors;
            b.CompletedMemberCount = 1;
            b.ErrorMessage = $"Without a usable result: suite 'Suite 22', round 1 (run #{repairedRunId}): index withheld.";
        });

        var services = new ServiceCollection();
        services.AddScoped(_ => new ApplicationDbContext(h.Options));
        services.AddScoped(sp => new BenchmarkBatteryAnalysisService(
            sp.GetRequiredService<ApplicationDbContext>(), NullLogger<BenchmarkBatteryAnalysisService>.Instance));
        services.AddSingleton(h.Service);
        await using var provider = services.BuildServiceProvider();
        var runManager = new BenchmarkRunManager();
        var orchestrator = new BenchmarkBatteryOrchestrator(
            provider.GetRequiredService<IServiceScopeFactory>(), runManager, NullLogger<BenchmarkBatteryOrchestrator>.Instance);

        await orchestrator.ReconcileAfterMemberChangeAsync(repairedRunId, ct);

        var row = await h.BatteryRunAsync();
        Assert.Equal(BenchmarkRunSeriesStatus.Completed, row.Status);
        Assert.Null(row.ErrorMessage);
        Assert.Null(row.StopReason);
        Assert.Equal(2, row.CompletedMemberCount);
        Assert.Equal((DateTime?)firstFinished, row.CompletedAtUtc);
        Assert.Null(runManager.OrchestratorOwner);
        Assert.False(orchestrator.IsAnalysing(h.BatteryRunId));

        await using (var db = new ApplicationDbContext(h.Options))
        {
            Assert.Equal(1, await db.BenchmarkBatteryAnalyses.CountAsync(a => a.BenchmarkBatteryRunId == h.BatteryRunId, ct));
        }

        await BatteryReportHarness.WaitUntilAsync(() => h.Writer.JobCalls == 1 && !h.Service.IsActive(h.BatteryRunId));
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Completed, await h.StatusAsync());
        Assert.Equal(3, (await h.DocumentsAsync()).Count);
    }

    /// <summary>
    /// With one of two unusable members repaired, the battery run stays CompletedWithErrors; its error
    /// message names only the slot still without a result, worded as the finish words it.
    /// </summary>
    [Fact]
    public async Task ReconcileAfterMemberChange_WithASlotStillUnusable_KeepsTheStatus_AndListsOnlyThatSlot()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = await CreateFixtureAsync();
        var batteryRun = await SeedBatteryRunAsync(fixture, BenchmarkRunSeriesStatus.CompletedWithErrors);
        var withheld = await SeedMemberAsync(fixture, batteryRun, suiteIndex: 0,
            status: BenchmarkRunStatus.CompletedWithErrors, qualityIndex: null, terminalFailures: 1);
        var repaired = await SeedMemberAsync(fixture, batteryRun, suiteIndex: 1,
            status: BenchmarkRunStatus.CompletedWithErrors, qualityIndex: 66, terminalFailures: 0);
        var firstFinished = DateTime.UtcNow.AddHours(-1);
        await MarkFinishedAsync(fixture, batteryRun.Id, BenchmarkRunSeriesStatus.CompletedWithErrors,
            $"Without a usable result: two slots, run #{withheld.BenchmarkRunId} and run #{repaired.BenchmarkRunId}.", firstFinished);

        await fixture.Orchestrator.ReconcileAfterMemberChangeAsync(repaired.BenchmarkRunId, ct);

        var row = await ReadBatteryRunAsync(fixture, batteryRun.Id);
        Assert.Equal(BenchmarkRunSeriesStatus.CompletedWithErrors, row.Status);
        Assert.Equal(1, row.CompletedMemberCount);
        Assert.Equal((DateTime?)firstFinished, row.CompletedAtUtc);
        Assert.Equal(
            $"Without a usable result: suite 'Suite A', round 1 (run #{withheld.BenchmarkRunId}): " +
            $"{BenchmarkBatteryPlanner.IndexWithheldProviderFailureReason}. Repair a run with Re-run Failed Questions and " +
            "recompute, or continue the battery run to replace it.",
            row.ErrorMessage);
        Assert.Null(fixture.RunManager.OrchestratorOwner);

        using var readback = CreateDbContext(fixture.DbName);
        Assert.Empty(await readback.BenchmarkBatteryAnalyses.ToListAsync(ct));
    }

    /// <summary>
    /// A held orchestrator claim skips the reconcile and leaves the claim with its holder: the battery
    /// run's own drive loop finishes it, and another orchestrator's claim is never taken over. Once
    /// the claim is free, the reconcile finishes the battery run.
    /// </summary>
    [Fact]
    public async Task ReconcileAfterMemberChange_SkipsWhileTheClaimIsHeld()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = await CreateFixtureAsync();
        var batteryRun = await SeedBatteryRunAsync(fixture, BenchmarkRunSeriesStatus.CompletedWithErrors);
        await SeedMemberAsync(fixture, batteryRun, suiteIndex: 0);
        var repaired = await SeedMemberAsync(fixture, batteryRun, suiteIndex: 1);
        await MarkFinishedAsync(fixture, batteryRun.Id, BenchmarkRunSeriesStatus.CompletedWithErrors, "Without a usable result: one slot.", DateTime.UtcNow.AddHours(-1));

        foreach (string owner in new[] { BenchmarkRunManager.BatteryOwner(batteryRun.Id), BenchmarkRunManager.SeriesOwner(3) })
        {
            Assert.True(fixture.RunManager.TryClaimOrchestrator(owner));

            await fixture.Orchestrator.ReconcileAfterMemberChangeAsync(repaired.BenchmarkRunId, ct);
            Assert.False(await fixture.Orchestrator.ReconcileBatteryRunAsync(batteryRun.Id, ct));

            Assert.Equal(BenchmarkRunSeriesStatus.CompletedWithErrors, (await ReadBatteryRunAsync(fixture, batteryRun.Id)).Status);
            Assert.Equal(owner, fixture.RunManager.OrchestratorOwner);
            fixture.RunManager.ReleaseOrchestrator(owner);
        }

        Assert.True(await fixture.Orchestrator.ReconcileBatteryRunAsync(batteryRun.Id, ct));
        Assert.Equal(BenchmarkRunSeriesStatus.Completed, (await ReadBatteryRunAsync(fixture, batteryRun.Id)).Status);
        Assert.Null(fixture.RunManager.OrchestratorOwner);
    }

    /// <summary>A Completed battery run is left alone, and a run in no battery run changes nothing.</summary>
    [Fact]
    public async Task ReconcileAfterMemberChange_LeavesACompletedBatteryRunAlone_AndARunInNoBatteryIsANoOp()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = await CreateFixtureAsync();
        var completed = await SeedBatteryRunAsync(fixture, BenchmarkRunSeriesStatus.Completed);
        var member = await SeedMemberAsync(fixture, completed, suiteIndex: 0, status: BenchmarkRunStatus.CompletedWithErrors, qualityIndex: null);

        await fixture.Orchestrator.ReconcileAfterMemberChangeAsync(member.BenchmarkRunId, ct);
        await fixture.Orchestrator.ReconcileAfterMemberChangeAsync(4242, ct);

        var row = await ReadBatteryRunAsync(fixture, completed.Id);
        Assert.Equal(BenchmarkRunSeriesStatus.Completed, row.Status);
        Assert.Equal(0, row.CompletedMemberCount);
        Assert.Null(fixture.RunManager.OrchestratorOwner);
    }

    /// <summary>
    /// Continue on a CompletedWithErrors battery run whose every slot is usable launches nothing: the
    /// drive loop finds no free slot and finishes it.
    /// </summary>
    [Fact]
    public async Task Continue_OnACompletedWithErrorsBatteryRunWithEverySlotUsable_FinishesIt()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = await CreateFixtureAsync();
        var batteryRun = await SeedBatteryRunAsync(fixture, BenchmarkRunSeriesStatus.CompletedWithErrors);
        await SeedMemberAsync(fixture, batteryRun, suiteIndex: 0);
        await SeedMemberAsync(fixture, batteryRun, suiteIndex: 1);
        await MarkFinishedAsync(fixture, batteryRun.Id, BenchmarkRunSeriesStatus.CompletedWithErrors, "Without a usable result: one slot.", DateTime.UtcNow.AddHours(-1));

        var result = await fixture.Orchestrator.ResumeAsync(batteryRun.Id, BenchmarkBatteryResumeMode.Continue, ct);
        Assert.Equal(BenchmarkBatteryStartOutcome.Started, result.Outcome);

        var row = await WaitUntilDrivenToTheEndAsync(fixture, batteryRun.Id);
        Assert.Equal(BenchmarkRunSeriesStatus.Completed, row.Status);
        Assert.Equal(2, row.CompletedMemberCount);
        Assert.NotNull(row.CompletedAtUtc);

        using var readback = CreateDbContext(fixture.DbName);
        Assert.Equal(2, await readback.BenchmarkRuns.CountAsync(ct));
        Assert.All(await readback.BenchmarkBatteryRunMembers.ToListAsync(ct), m => Assert.False(m.Superseded));
    }

    /// <summary>
    /// A member run in flight while the battery run is not driven is a repair: its slot is held, so
    /// Continue is a conflict naming the run, and nothing is claimed or changed.
    /// </summary>
    [Fact]
    public async Task Continue_WhileAMemberIsBeingRerun_IsAConflict_NamingTheRun()
    {
        var ct = TestContext.Current.CancellationToken;
        var fixture = await CreateFixtureAsync();
        var batteryRun = await SeedBatteryRunAsync(fixture, BenchmarkRunSeriesStatus.CompletedWithErrors);
        await SeedMemberAsync(fixture, batteryRun, suiteIndex: 0);
        var rerunning = await SeedMemberAsync(fixture, batteryRun, suiteIndex: 1, status: BenchmarkRunStatus.Running, qualityIndex: null);

        var result = await fixture.Orchestrator.ResumeAsync(batteryRun.Id, BenchmarkBatteryResumeMode.Continue, ct);

        Assert.Equal(BenchmarkBatteryStartOutcome.Conflict, result.Outcome);
        Assert.Equal(
            $"Run #{rerunning.BenchmarkRunId} is being re-run; the battery run follows it when the re-run finishes.",
            result.Error);
        Assert.False(fixture.Orchestrator.IsDriving(batteryRun.Id));
        Assert.Null(fixture.RunManager.OrchestratorOwner);

        using var readback = CreateDbContext(fixture.DbName);
        Assert.Equal(BenchmarkRunSeriesStatus.CompletedWithErrors,
            (await readback.BenchmarkBatteryRuns.SingleAsync(b => b.Id == batteryRun.Id, ct)).Status);
        Assert.False((await readback.BenchmarkBatteryRunMembers.SingleAsync(m => m.Id == rerunning.Id, ct)).Superseded);
    }

    // --- The two guards ------------------------------------------------------------------------

    private static BenchmarkRun GuardRun(long id, long suiteId, string modelId = "claude-opus-5", string harness = "45", string guides = "guides")
    {
        var run = new BenchmarkRun
        {
            Id = id,
            BenchmarkSuiteId = suiteId,
            BenchmarkSuiteIdUsed = suiteId,
            SuiteName = $"Suite {suiteId}",
            Status = BenchmarkRunStatus.Completed,
            QualityIndex = 70,
            TestedModelSnapshot = BenchmarkModelSnapshots.Model(
                provider: "Anthropic", modelId: modelId, displayName: modelId,
                parallelExecutionMode: ParallelExecutionMode.Enabled),
            AssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "OpenAI", modelId: "gpt-5", displayName: "Assessor"),
            CandidatePromptOptionsJson = new BenchmarkCandidatePromptOptions().ToCanonicalJson(),
            CandidateSystemPromptSha256 = "prompt",
            ToolGuidesSha256 = guides,
            KnowledgeBaseHeadSha = "kb",
            WikiHeadSha = "wiki",
            SourceCodeHeadSha = "source",
            HarnessVersion = harness,
            ScoringMethodVersion = 13,
            ScoringProfileId = 1,
            MaxParallelQuestionsUsed = 1
        };

        run.Answers.Add(new BenchmarkRunAnswer
        {
            BenchmarkRunId = id,
            BenchmarkQuestionId = suiteId * 100 + 1,
            BenchmarkQuestionIdUsed = suiteId * 100 + 1,
            ItemRevisionUsed = 1,
            AssessedDifficulty = 40,
            OrderIndex = 1,
            QuestionText = "Q1",
            Status = BenchmarkAnswerStatus.Ok,
            QualityScore = 70
        });

        return run;
    }

    private static BenchmarkBatteryRunMember Member(int suiteIndex, int round, long runId, bool superseded = false, string? guardFailure = null)
        => new() { SuiteIndex = suiteIndex, Round = round, BenchmarkRunId = runId, Superseded = superseded, GuardFailure = guardFailure };

    [Fact]
    public void FingerprintGuard_PassesAMatchingRun_AndNamesAMovedHash()
    {
        var recorded = new BenchmarkInstrumentFingerprint("prompt", "guides", "kb", "wiki", "source");

        Assert.Null(BenchmarkBatteryOrchestrator.FingerprintGuardFailure(recorded, GuardRun(1, 10)));

        string? failure = BenchmarkBatteryOrchestrator.FingerprintGuardFailure(recorded, GuardRun(1, 10, guides: "moved"));
        Assert.NotNull(failure);
        Assert.Contains("ToolGuidesSha256", failure);
    }

    /// <summary>A hash missing on either side is not recorded, and never counts as a difference.</summary>
    [Fact]
    public void FingerprintGuard_IgnoresAHashMissingOnEitherSide()
    {
        var run = GuardRun(1, 10);
        run.WikiHeadSha = null;

        Assert.Null(BenchmarkBatteryOrchestrator.FingerprintGuardFailure(null, run));
        Assert.Null(BenchmarkBatteryOrchestrator.FingerprintGuardFailure(
            new BenchmarkInstrumentFingerprint(null, "guides", null, "wiki-at-start", null), run));
        Assert.Empty(BenchmarkBatteryOrchestrator.FingerprintDifferences(
            new BenchmarkInstrumentFingerprint("a", "b", "c", "d", "e"), null));
    }

    [Fact]
    public void ComparabilityGuard_PassesOneModelUnderOneInstrument_AcrossSuites()
    {
        var members = new List<(BenchmarkBatteryRunMember, BenchmarkRun)>
        {
            (Member(0, 1, 1), GuardRun(1, 10)),
            (Member(1, 1, 2), GuardRun(2, 20))
        };

        Assert.Null(BenchmarkBatteryOrchestrator.ComparabilityGuardFailure(members));
    }

    [Fact]
    public void ComparabilityGuard_RefusesAHarnessChangeBetweenSuites()
    {
        var members = new List<(BenchmarkBatteryRunMember, BenchmarkRun)>
        {
            (Member(0, 1, 1), GuardRun(1, 10, harness: "45")),
            (Member(1, 1, 2), GuardRun(2, 20, harness: "46"))
        };

        string? failure = BenchmarkBatteryOrchestrator.ComparabilityGuardFailure(members);
        Assert.NotNull(failure);
        Assert.Contains("refused", failure);
    }

    /// <summary>Only usable members are judged: a superseded or guarded member cannot refuse the composite.</summary>
    [Fact]
    public void ComparabilityGuard_IgnoresMembersThatAreNotUsable()
    {
        var members = new List<(BenchmarkBatteryRunMember, BenchmarkRun)>
        {
            (Member(0, 1, 1), GuardRun(1, 10)),
            (Member(1, 1, 2), GuardRun(2, 20)),
            (Member(1, 2, 3, superseded: true), GuardRun(3, 20, modelId: "other-model")),
            (Member(0, 2, 4, guardFailure: "moved"), GuardRun(4, 10, harness: "46"))
        };

        Assert.Null(BenchmarkBatteryOrchestrator.ComparabilityGuardFailure(members));
        Assert.Null(BenchmarkBatteryOrchestrator.ComparabilityGuardFailure(
            new List<(BenchmarkBatteryRunMember, BenchmarkRun)>()));
    }

    // --- Member count and resume pre-checks -----------------------------------------------------

    [Fact]
    public void CompletedMemberCount_CountsSlotsWithAUsableMember()
    {
        var withheld = GuardRun(3, 20);
        withheld.Status = BenchmarkRunStatus.CompletedWithErrors;
        withheld.QualityIndex = null;
        withheld.TerminalFailureAnswerCount = 1;

        var failed = GuardRun(4, 10);
        failed.Status = BenchmarkRunStatus.Failed;

        var members = new List<(BenchmarkBatteryRunMember, BenchmarkRun?)>
        {
            (Member(0, 1, 1), GuardRun(1, 10)),
            (Member(1, 1, 2), GuardRun(2, 20)),
            (Member(1, 1, 9, superseded: true), GuardRun(9, 20)),
            (Member(0, 2, 3), withheld),
            (Member(1, 2, 4), failed),
            (Member(0, 2, 5, guardFailure: "moved"), GuardRun(5, 10)),
            (Member(0, 3, 6), GuardRun(6, 10)),
            (Member(1, 2, 7), null)
        };

        Assert.Equal(2, BenchmarkBatteryOrchestrator.RecomputeCompletedMemberCount(members, suiteCount: 2, runsPerSuite: 2));
    }

    [Theory]
    [InlineData(BenchmarkRunSeriesStatus.Stopped, true)]
    [InlineData(BenchmarkRunSeriesStatus.CompletedWithErrors, true)]
    [InlineData(BenchmarkRunSeriesStatus.Completed, false)]
    [InlineData(BenchmarkRunSeriesStatus.Cancelled, false)]
    [InlineData(BenchmarkRunSeriesStatus.Failed, false)]
    [InlineData(BenchmarkRunSeriesStatus.Running, false)]
    [InlineData(BenchmarkRunSeriesStatus.Pending, false)]
    [InlineData(BenchmarkRunSeriesStatus.WaitingForCap, false)]
    public void ResumeStatus_AdmitsStopped_AndCompletedWithErrorsEvenWithEverySlotUsable(
        BenchmarkRunSeriesStatus status, bool resumable)
    {
        Assert.Equal(resumable, BenchmarkBatteryOrchestrator.ResumeStatusRefusal(status) == null);
        Assert.Equal(resumable, BenchmarkBatteryOrchestrator.ResumeStatusRefusal(status, Array.Empty<long>()) == null);
    }

    /// <summary>A member run in flight outside the drive loop holds its slot: a resumable status is refused, naming the run.</summary>
    [Fact]
    public void ResumeStatus_RefusesWhileAMemberIsBeingRerun()
    {
        Assert.Equal(
            "Run #7 is being re-run; the battery run follows it when the re-run finishes.",
            BenchmarkBatteryOrchestrator.ResumeStatusRefusal(BenchmarkRunSeriesStatus.CompletedWithErrors, new long[] { 9, 7 }));
        Assert.Contains("cannot be resumed",
            BenchmarkBatteryOrchestrator.ResumeStatusRefusal(BenchmarkRunSeriesStatus.Completed, new long[] { 7 }));

        var running = GuardRun(7, 10);
        running.Status = BenchmarkRunStatus.Running;
        var supersededRunning = GuardRun(8, 20);
        supersededRunning.Status = BenchmarkRunStatus.Running;
        var members = new List<(BenchmarkBatteryRunMember, BenchmarkRun?)>
        {
            (Member(0, 1, 1), GuardRun(1, 10)),
            (Member(1, 1, 7), running),
            (Member(1, 1, 8, superseded: true), supersededRunning),
            (Member(0, 2, 9), null)
        };

        Assert.Equal(new long[] { 7 }, BenchmarkBatteryOrchestrator.RepairingRunIds(members));
    }

    [Fact]
    public void ResumePreChecks_NameForeignScoringMethodsHarnessesAndGuardFailures()
    {
        var current = GuardRun(1, 10, harness: "45");
        current.ScoringMethodVersion = 13;
        var old = GuardRun(2, 20, harness: "44");
        old.ScoringMethodVersion = 12;

        Assert.Equal(new[] { 12 }, BenchmarkBatteryOrchestrator.ForeignScoringMethods(new[] { current, old }, 13));
        Assert.Empty(BenchmarkBatteryOrchestrator.ForeignScoringMethods(new[] { current }, 13));

        Assert.Null(BenchmarkBatteryOrchestrator.HarnessVersionRefusal(new[] { current }, "45"));
        Assert.Null(BenchmarkBatteryOrchestrator.HarnessVersionRefusal(Array.Empty<BenchmarkRun>(), "45"));
        Assert.Contains("44", BenchmarkBatteryOrchestrator.HarnessVersionRefusal(new[] { current, old }, "45"));

        Assert.Null(BenchmarkBatteryOrchestrator.GuardFailureRefusal(new[] { Member(0, 1, 1), Member(1, 1, 2, superseded: true, guardFailure: "old") }));
        Assert.Contains("moved", BenchmarkBatteryOrchestrator.GuardFailureRefusal(new[] { Member(0, 1, 1), Member(1, 1, 2, guardFailure: "moved") }));
    }

    [Theory]
    [InlineData(2, 1, 60, 20, false, true)]
    [InlineData(2, 31, 60, 100, true, false)]
    [InlineData(2, 30, 60, 100, true, true)]
    [InlineData(2, 11, 60, 20, false, false)]
    [InlineData(2, 11, 60, 20, true, true)]
    public void LaunchBound_RefusesAboveTheMaximum_AndAboveTheDailyCapWithoutCapWait(
        int suites, int runsPerSuite, int maxMembers, int maxRunsPerDay, bool allowCapWait, bool accepted)
    {
        Assert.Equal(accepted, BenchmarkBatteryOrchestrator.LaunchBoundRefusal(
            suites, runsPerSuite, attachedCount: 0, maxMembers, maxRunsPerDay, allowCapWait) == null);
    }

    // --- Stored state ---------------------------------------------------------------------------

    /// <summary>
    /// The battery's own deserializer, not the series': a stored request without the
    /// source-code-references member reads as disallowed, a user's default, and every read is a
    /// fresh object the caller may give its own suite id.
    /// </summary>
    [Fact]
    public void StoredRequest_ResolvesLikeABatteryStoresIt_AndIsFreshPerLaunch()
    {
        var batteryRun = new BenchmarkBatteryRun
        {
            StartRequestJson = "{\"SuiteId\":1,\"TestedModelConfigurationId\":1,\"AssessorModelConfigurationId\":2,\"RunCount\":3}"
        };

        var stored = BenchmarkBatteryOrchestrator.DeserializeRequest(batteryRun);
        Assert.NotNull(stored);
        Assert.False(stored!.AllowSourceCodeReferences);
        Assert.Equal(1, stored.RunCount);

        var forA = BenchmarkBatteryOrchestrator.RequestForSuite(batteryRun, 10);
        var forB = BenchmarkBatteryOrchestrator.RequestForSuite(batteryRun, 20);
        Assert.Equal(10, forA!.SuiteId);
        Assert.Equal(20, forB!.SuiteId);
        Assert.NotSame(forA, forB);

        Assert.Null(BenchmarkBatteryOrchestrator.DeserializeRequest(new BenchmarkBatteryRun { StartRequestJson = "not json" }));
    }

    [Fact]
    public void Fingerprints_RoundTrip_AndAnUnrecordedSuiteIsAbsent()
    {
        var written = BenchmarkBatteryOrchestrator.WriteFingerprints(new Dictionary<int, BenchmarkInstrumentFingerprint?>
        {
            [0] = new("prompt", "guides", null, "wiki", "source"),
            [1] = null
        });

        var read = BenchmarkBatteryOrchestrator.ReadFingerprints(written);

        Assert.Single(read);
        Assert.Equal("guides", read[0].ToolGuidesSha256);
        Assert.Null(read[0].KnowledgeBaseHeadSha);
        Assert.Empty(BenchmarkBatteryOrchestrator.ReadFingerprints(null));
    }
}
