namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.Benchmarking;
using Overseer.Tests.Helpers;
using Xunit;

/// <summary>
/// The battery API: definitions (create, edit, revision and hash), the start refusal of a broken
/// battery, the outcome-to-status mapping, attaching existing runs (reuse preview, attach,
/// candidates), delete while a battery run is active, deleting a battery run with and without its
/// member runs, the battery-run grid and identity, its live cost and mean model time, the list
/// sizes, the leaderboard and the
/// ranked-result counts, and the <see cref="AdminBenchmarkController"/> additions (re-run refusal
/// under an orchestrator claim, the stop-reason text, the run-summary battery fields, the run list size).
/// </summary>
public class AdminBenchmarkBatteriesControllerTests
{
    private const string OwnerId = "admin-1";

    // --- Fixture ------------------------------------------------------------------------------

    private sealed record Fixture(
        AdminBenchmarkBatteriesController Controller,
        ApplicationDbContext Db,
        BenchmarkRunManager RunManager,
        BenchmarkSuite SuiteA,
        BenchmarkSuite SuiteB,
        BenchmarkSuite SuiteC);

    private static ApplicationDbContext CreateDbContext(string name)
        => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options);

    private static ControllerContext UserContext() => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, OwnerId) }, "TestAuth"))
        }
    };

    private static BenchmarkSuite Suite(string name, params int?[] difficulties)
    {
        var suite = new BenchmarkSuite { Name = name, Description = "Desc" };
        for (int i = 0; i < difficulties.Length; i++)
        {
            suite.Questions.Add(new BenchmarkQuestion
            {
                QuestionText = $"{name} Q{i + 1}",
                OrderIndex = i + 1,
                Difficulty = BenchmarkDifficulty.Simple,
                AssessedDifficulty = difficulties[i]
            });
        }
        return suite;
    }

    /// <summary>
    /// Three suites (A: difficulties 40 and 60; B: one unassessed question; C: 30), and a controller
    /// whose orchestrator reads the same in-memory database through its own scopes.
    /// </summary>
    private static async Task<Fixture> CreateFixtureAsync()
    {
        string dbName = Guid.NewGuid().ToString();

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Benchmark:Compliance:MaxRunsPerDay", "20" },
                { "Benchmark:Compliance:MaxRunsPerHour", "5" },
                { "Benchmark:Battery:MaxMembers", "60" }
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton(config);
        services.AddScoped(_ => CreateDbContext(dbName));
        services.AddScoped<BenchmarkComplianceGuard>();
        var provider = services.BuildServiceProvider();

        var runManager = new BenchmarkRunManager();
        var orchestrator = new BenchmarkBatteryOrchestrator(
            provider.GetRequiredService<IServiceScopeFactory>(),
            runManager,
            NullLogger<BenchmarkBatteryOrchestrator>.Instance);

        var db = CreateDbContext(dbName);
        var suiteA = Suite("Suite A", 40, 60);
        var suiteB = Suite("Suite B", new int?[] { null });
        var suiteC = Suite("Suite C", 30);
        db.BenchmarkSuites.AddRange(suiteA, suiteB, suiteC);
        await db.SaveChangesAsync();

        var controller = new AdminBenchmarkBatteriesController(
            db,
            orchestrator,
            new BenchmarkBatteryAnalysisService(db, NullLogger<BenchmarkBatteryAnalysisService>.Instance),
            new BenchmarkBatteryLeaderboardService(db),
            runManager)
        {
            ControllerContext = UserContext()
        };

        return new Fixture(controller, db, runManager, suiteA, suiteB, suiteC);
    }

    private static CreateBenchmarkBatteryRequest CreateRequest(string name, params long[] suiteIds) => new()
    {
        Name = name,
        WeightingScheme = BenchmarkBatteryWeightingScheme.DifficultyMass,
        SuiteIds = suiteIds.ToList()
    };

    private static UpdateBenchmarkBatteryRequest UpdateRequest(
        string name,
        BenchmarkBatteryWeightingScheme scheme,
        IEnumerable<long> suiteIds,
        List<double?>? customWeights = null) => new()
    {
        Name = name,
        WeightingScheme = scheme,
        SuiteIds = suiteIds.ToList(),
        CustomWeights = customWeights
    };

    private static BenchmarkBatteryDto ReadBattery(IActionResult result)
        => Assert.IsType<BenchmarkBatteryDto>(Assert.IsType<OkObjectResult>(result).Value);

    private static string Hash(BenchmarkBatteryWeightingScheme scheme, params (long SuiteId, double? Weight)[] suites)
        => BenchmarkBatteryDefinition.ComputeSha256(scheme, suites);

    /// <summary>A battery saved directly, bypassing the controller.</summary>
    private static async Task<BenchmarkBattery> SeedBatteryAsync(
        ApplicationDbContext db,
        string name,
        params (long? SuiteId, string SuiteName)[] suites)
    {
        var battery = new BenchmarkBattery
        {
            Name = name,
            WeightingScheme = BenchmarkBatteryWeightingScheme.DifficultyMass,
            DefinitionSha256 = new string('0', 64)
        };
        for (int i = 0; i < suites.Length; i++)
        {
            battery.Suites.Add(new BenchmarkBatterySuite
            {
                BenchmarkSuiteId = suites[i].SuiteId,
                SuiteName = suites[i].SuiteName,
                OrderIndex = i
            });
        }

        db.BenchmarkBatteries.Add(battery);
        await db.SaveChangesAsync();
        return battery;
    }

    /// <summary>A battery run row over suites A and B, as a start would have written it.</summary>
    private static async Task<BenchmarkBatteryRun> SeedBatteryRunAsync(
        Fixture fixture,
        long? batteryId,
        BenchmarkRunSeriesStatus status,
        string? definitionSha256 = null,
        int runsPerSuite = 1)
    {
        var definition = new BenchmarkBatteryDefinition(
            batteryId,
            "Pair",
            1,
            BenchmarkBatteryWeightingScheme.DifficultyMass,
            new[]
            {
                new BenchmarkBatteryDefinitionSuite(0, fixture.SuiteA.Id, fixture.SuiteA.Name, null),
                new BenchmarkBatteryDefinitionSuite(1, fixture.SuiteB.Id, fixture.SuiteB.Name, null)
            });

        var batteryRun = new BenchmarkBatteryRun
        {
            BenchmarkBatteryId = batteryId,
            BatteryName = "Pair",
            DefinitionJson = definition.ToJson(),
            DefinitionSha256 = definitionSha256 ?? definition.DefinitionSha256,
            RunsPerSuite = runsPerSuite,
            RequestedMemberCount = 2 * runsPerSuite,
            Status = status,
            StopReason = status == BenchmarkRunSeriesStatus.Stopped ? BenchmarkRunSeriesStopReason.MemberFailed : null,
            StartRequestJson = JsonSerializer.Serialize(new StartBenchmarkRunRequest
            {
                SuiteId = fixture.SuiteA.Id,
                TestedModelConfigurationId = 1,
                AssessorModelConfigurationId = 2
            }),
            StartedAtUtc = DateTime.UtcNow.AddHours(-1)
        };

        fixture.Db.BenchmarkBatteryRuns.Add(batteryRun);
        await fixture.Db.SaveChangesAsync();
        return batteryRun;
    }

    private static async Task<BenchmarkRun> SeedMemberAsync(
        Fixture fixture,
        BenchmarkBatteryRun batteryRun,
        int suiteIndex,
        int round = 1,
        BenchmarkRunStatus status = BenchmarkRunStatus.Completed,
        int? qualityIndex = 70)
    {
        var suite = suiteIndex == 0 ? fixture.SuiteA : fixture.SuiteB;
        var run = new BenchmarkRun
        {
            BenchmarkSuiteId = suite.Id,
            BenchmarkSuiteIdUsed = suite.Id,
            SuiteName = suite.Name,
            TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Anthropic", modelId: "claude-opus-5", displayName: "Opus Under Test"),
            AssessorModelSnapshot = BenchmarkModelSnapshots.Assessor(),
            Status = status,
            QualityIndex = qualityIndex,
            TotalQuestionCount = 1,
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-30)
        };
        fixture.Db.BenchmarkRuns.Add(run);
        await fixture.Db.SaveChangesAsync();

        fixture.Db.BenchmarkBatteryRunMembers.Add(new BenchmarkBatteryRunMember
        {
            BenchmarkBatteryRunId = batteryRun.Id,
            BenchmarkRunId = run.Id,
            SuiteIndex = suiteIndex,
            Round = round,
            Origin = BenchmarkBatteryMemberOrigin.Launched
        });
        await fixture.Db.SaveChangesAsync();
        return run;
    }

    // --- Create and edit ------------------------------------------------------------------------

    [Fact]
    public async Task Create_StartsAtRevisionOne_WithTheDefinitionHashAndAWeightPreviewPerScheme()
    {
        var f = await CreateFixtureAsync();

        var dto = ReadBattery(await f.Controller.CreateBattery(
            CreateRequest("  Pair  ", f.SuiteA.Id, f.SuiteB.Id), TestContext.Current.CancellationToken));

        Assert.Equal("Pair", dto.Name);
        Assert.Equal(1, dto.Revision);
        Assert.Equal(Hash(BenchmarkBatteryWeightingScheme.DifficultyMass, (f.SuiteA.Id, null), (f.SuiteB.Id, null)), dto.DefinitionSha256);
        Assert.Empty(dto.BrokenSuiteNames);
        Assert.Empty(dto.ValidationErrors);

        Assert.Equal(new long?[] { f.SuiteA.Id, f.SuiteB.Id }, dto.Suites.Select(s => s.SuiteId));
        Assert.Equal(2, dto.Suites[0].QuestionCount);
        Assert.True(dto.Suites[0].DifficultyFullyAssessed);
        Assert.Equal(100.0, dto.Suites[0].DifficultyMass, 6);
        Assert.False(dto.Suites[1].DifficultyFullyAssessed);
        Assert.Equal(50.0, dto.Suites[1].DifficultyMass, 6);

        Assert.Equal(4, dto.WeightPreviews.Count);
        var declared = Assert.Single(dto.WeightPreviews, p => p.Declared);
        Assert.Equal(BenchmarkBatteryWeightingScheme.DifficultyMass, declared.Scheme);
        Assert.Equal(2.0 / 3.0, declared.Weights[0], 6);
        Assert.Equal(1.0 / 3.0, declared.Weights[1], 6);
        Assert.Equal(new[] { 0.5, 0.5 }, dto.WeightPreviews.Single(p => p.Scheme == BenchmarkBatteryWeightingScheme.Equal).Weights);
        Assert.Empty(dto.WeightPreviews.Single(p => p.Scheme == BenchmarkBatteryWeightingScheme.Custom).Weights);

        var stored = await f.Db.BenchmarkBatteries.Include(b => b.Suites).SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(OwnerId, stored.CreatedByUserId);
        Assert.Equal(dto.DefinitionSha256, stored.DefinitionSha256);
    }

    [Fact]
    public async Task Create_RefusesASingleSuite_ADuplicateName_AndAMissingSuite()
    {
        var f = await CreateFixtureAsync();
        var ct = TestContext.Current.CancellationToken;

        Assert.IsType<BadRequestObjectResult>(await f.Controller.CreateBattery(CreateRequest("One", f.SuiteA.Id), ct));
        Assert.IsType<BadRequestObjectResult>(await f.Controller.CreateBattery(CreateRequest("Missing", f.SuiteA.Id, 999_999), ct));

        ReadBattery(await f.Controller.CreateBattery(CreateRequest("Pair", f.SuiteA.Id, f.SuiteB.Id), ct));
        Assert.IsType<BadRequestObjectResult>(await f.Controller.CreateBattery(CreateRequest("Pair", f.SuiteA.Id, f.SuiteC.Id), ct));

        Assert.Equal(1, await f.Db.BenchmarkBatteries.CountAsync(ct));
    }

    [Fact]
    public async Task Update_NameOnly_KeepsRevisionAndHash()
    {
        var f = await CreateFixtureAsync();
        var ct = TestContext.Current.CancellationToken;
        var created = ReadBattery(await f.Controller.CreateBattery(CreateRequest("Pair", f.SuiteA.Id, f.SuiteB.Id), ct));

        var dto = ReadBattery(await f.Controller.UpdateBattery(
            created.Id,
            UpdateRequest("Renamed", BenchmarkBatteryWeightingScheme.DifficultyMass, new[] { f.SuiteA.Id, f.SuiteB.Id }),
            ct));

        Assert.Equal("Renamed", dto.Name);
        Assert.Equal(1, dto.Revision);
        Assert.Equal(created.DefinitionSha256, dto.DefinitionSha256);
    }

    [Fact]
    public async Task Update_SuiteOrSchemeChange_BumpsRevisionAndRecomputesTheHash()
    {
        var f = await CreateFixtureAsync();
        var ct = TestContext.Current.CancellationToken;
        var created = ReadBattery(await f.Controller.CreateBattery(CreateRequest("Pair", f.SuiteA.Id, f.SuiteB.Id), ct));

        var replaced = ReadBattery(await f.Controller.UpdateBattery(
            created.Id,
            UpdateRequest("Pair", BenchmarkBatteryWeightingScheme.DifficultyMass, new[] { f.SuiteC.Id, f.SuiteA.Id }),
            ct));

        Assert.Equal(2, replaced.Revision);
        Assert.Equal(Hash(BenchmarkBatteryWeightingScheme.DifficultyMass, (f.SuiteA.Id, null), (f.SuiteC.Id, null)), replaced.DefinitionSha256);
        Assert.Equal(new long?[] { f.SuiteC.Id, f.SuiteA.Id }, replaced.Suites.Select(s => s.SuiteId));

        var custom = ReadBattery(await f.Controller.UpdateBattery(
            created.Id,
            UpdateRequest("Pair", BenchmarkBatteryWeightingScheme.Custom, new[] { f.SuiteC.Id, f.SuiteA.Id }, new List<double?> { 2.0, 1.0 }),
            ct));

        Assert.Equal(3, custom.Revision);
        Assert.Equal(Hash(BenchmarkBatteryWeightingScheme.Custom, (f.SuiteC.Id, 2.0), (f.SuiteA.Id, 1.0)), custom.DefinitionSha256);
        Assert.Equal(2, await f.Db.BenchmarkBatterySuites.CountAsync(s => s.BenchmarkBatteryId == created.Id, ct));
    }

    [Fact]
    public async Task Update_RefusesCustomWeightsThatDoNotMatchTheSuites()
    {
        var f = await CreateFixtureAsync();
        var ct = TestContext.Current.CancellationToken;
        var created = ReadBattery(await f.Controller.CreateBattery(CreateRequest("Pair", f.SuiteA.Id, f.SuiteB.Id), ct));

        var result = await f.Controller.UpdateBattery(
            created.Id,
            UpdateRequest("Pair", BenchmarkBatteryWeightingScheme.Custom, new[] { f.SuiteA.Id, f.SuiteB.Id }, new List<double?> { 1.0 }),
            ct);

        Assert.IsType<BadRequestObjectResult>(result);
        var stored = await f.Db.BenchmarkBatteries.AsNoTracking().SingleAsync(ct);
        Assert.Equal(1, stored.Revision);
        Assert.Equal(BenchmarkBatteryWeightingScheme.DifficultyMass, stored.WeightingScheme);
    }

    [Fact]
    public async Task BrokenBattery_NamesItsDeletedSuite_AndAnEditRepairsIt()
    {
        var f = await CreateFixtureAsync();
        var ct = TestContext.Current.CancellationToken;
        var battery = await SeedBatteryAsync(f.Db, "Broken", (f.SuiteA.Id, f.SuiteA.Name), (null, "Gone Suite"));

        var broken = ReadBattery(await f.Controller.GetBattery(battery.Id, ct));
        Assert.Equal(new[] { "Gone Suite" }, broken.BrokenSuiteNames);
        Assert.NotEmpty(broken.ValidationErrors);
        Assert.True(broken.Suites[1].Deleted);

        var repaired = ReadBattery(await f.Controller.UpdateBattery(
            battery.Id,
            UpdateRequest("Broken", BenchmarkBatteryWeightingScheme.DifficultyMass, new[] { f.SuiteA.Id, f.SuiteC.Id }),
            ct));

        Assert.Empty(repaired.BrokenSuiteNames);
        Assert.Empty(repaired.ValidationErrors);
        Assert.Equal(2, repaired.Revision);
        Assert.Equal(Hash(BenchmarkBatteryWeightingScheme.DifficultyMass, (f.SuiteA.Id, null), (f.SuiteC.Id, null)), repaired.DefinitionSha256);
    }

    // --- Start and outcome mapping ----------------------------------------------------------------

    [Fact]
    public async Task Start_BrokenBattery_Returns400_AndCreatesNothing()
    {
        var f = await CreateFixtureAsync();
        var ct = TestContext.Current.CancellationToken;
        var battery = await SeedBatteryAsync(f.Db, "Broken", (f.SuiteA.Id, f.SuiteA.Name), (null, "Gone Suite"));

        var result = await f.Controller.StartBatteryRun(new StartBenchmarkBatteryRunRequest
        {
            BatteryId = battery.Id,
            Run = new StartBenchmarkRunRequest { TestedModelConfigurationId = 1, AssessorModelConfigurationId = 2 }
        }, ct);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Gone Suite", Assert.IsType<string>(badRequest.Value));
        Assert.Equal(0, await f.Db.BenchmarkBatteryRuns.CountAsync(ct));
        Assert.Null(f.RunManager.OrchestratorOwner);
    }

    [Theory]
    [InlineData(BenchmarkBatteryStartOutcome.Started, StatusCodes.Status202Accepted)]
    [InlineData(BenchmarkBatteryStartOutcome.Conflict, StatusCodes.Status409Conflict)]
    [InlineData(BenchmarkBatteryStartOutcome.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(BenchmarkBatteryStartOutcome.Invalid, StatusCodes.Status400BadRequest)]
    [InlineData(BenchmarkBatteryStartOutcome.SpendDenied, StatusCodes.Status429TooManyRequests)]
    [InlineData(BenchmarkBatteryStartOutcome.InstrumentChanged, StatusCodes.Status409Conflict)]
    [InlineData(BenchmarkBatteryStartOutcome.SameProviderNotAcknowledged, StatusCodes.Status409Conflict)]
    [InlineData(BenchmarkBatteryStartOutcome.TooManyMembers, StatusCodes.Status400BadRequest)]
    public void StartOutcomes_MapToTheSeriesStatusCodes(BenchmarkBatteryStartOutcome outcome, int expectedStatus)
    {
        var result = AdminBenchmarkBatteriesController.StartResultToActionResult(new BenchmarkBatteryStartResult
        {
            Outcome = outcome,
            BatteryRunId = 7,
            Error = "refused",
            ChangedInstrumentHashes = new[] { "ToolGuidesSha256 (suite 'A')" }
        });

        Assert.Equal(expectedStatus, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public void InstrumentChangedOutcome_CarriesTheMovedHashes()
    {
        var result = AdminBenchmarkBatteriesController.StartResultToActionResult(new BenchmarkBatteryStartResult
        {
            Outcome = BenchmarkBatteryStartOutcome.InstrumentChanged,
            BatteryRunId = 7,
            Error = "moved",
            ChangedInstrumentHashes = new[] { "ToolGuidesSha256 (suite 'A')" }
        });

        string json = JsonSerializer.Serialize(Assert.IsAssignableFrom<ObjectResult>(result).Value);
        Assert.Contains("\"instrumentChanged\":true", json);
        Assert.Contains("\"batteryRunId\":7", json);
        Assert.Contains("ToolGuidesSha256 (suite", json);
    }

    // --- Attaching existing runs --------------------------------------------------------------------

    /// <summary>A finished run of suite A by the stored request's tested configuration (id 1), in no battery run.</summary>
    private static async Task<BenchmarkRun> SeedLooseRunAsync(Fixture fixture, int? qualityIndex = 70)
    {
        var run = new BenchmarkRun
        {
            BenchmarkSuiteId = fixture.SuiteA.Id,
            BenchmarkSuiteIdUsed = fixture.SuiteA.Id,
            SuiteName = fixture.SuiteA.Name,
            TestedModelConfigurationId = 1,
            TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Anthropic", modelId: "claude-opus-5", displayName: "Opus Under Test"),
            AssessorModelSnapshot = BenchmarkModelSnapshots.Assessor(),
            Status = qualityIndex.HasValue ? BenchmarkRunStatus.Completed : BenchmarkRunStatus.CompletedWithErrors,
            QualityIndex = qualityIndex,
            ScoringMethodVersion = BenchmarkAssessmentPrompt.ScoringMethodVersion,
            HarnessVersion = BenchmarkAssessmentPrompt.HarnessVersion,
            TotalQuestionCount = 1,
            StartedAtUtc = DateTime.UtcNow.AddHours(-3)
        };
        fixture.Db.BenchmarkRuns.Add(run);
        await fixture.Db.SaveChangesAsync();
        return run;
    }

    [Theory]
    [InlineData(BenchmarkBatteryAttachOutcome.Ok, StatusCodes.Status200OK)]
    [InlineData(BenchmarkBatteryAttachOutcome.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(BenchmarkBatteryAttachOutcome.Conflict, StatusCodes.Status409Conflict)]
    [InlineData(BenchmarkBatteryAttachOutcome.Ineligible, StatusCodes.Status400BadRequest)]
    public void AttachOutcomes_MapToStatusCodes(BenchmarkBatteryAttachOutcome outcome, int expectedStatus)
    {
        var result = AdminBenchmarkBatteriesController.AttachResultToActionResult(
            new BenchmarkBatteryAttachResult { Outcome = outcome, Error = "refused" });

        Assert.Equal(expectedStatus, Assert.IsAssignableFrom<IStatusCodeActionResult>(result).StatusCode);
    }

    [Fact]
    public async Task ReusePreview_OfAnUnknownBattery_Returns404_AndOfABrokenOne_Returns400()
    {
        var f = await CreateFixtureAsync();
        var ct = TestContext.Current.CancellationToken;
        var broken = await SeedBatteryAsync(f.Db, "Broken", (f.SuiteA.Id, f.SuiteA.Name), (null, "Gone Suite"));
        var run = new StartBenchmarkRunRequest { TestedModelConfigurationId = 1, AssessorModelConfigurationId = 2 };

        Assert.IsType<NotFoundObjectResult>(await f.Controller.PreviewBatteryReuse(
            new StartBenchmarkBatteryRunRequest { BatteryId = 4242, Run = run }, ct));

        var badRequest = Assert.IsType<BadRequestObjectResult>(await f.Controller.PreviewBatteryReuse(
            new StartBenchmarkBatteryRunRequest { BatteryId = broken.Id, Run = run }, ct));
        Assert.Contains("Gone Suite", Assert.IsType<string>(badRequest.Value));
        Assert.Equal(0, await f.Db.BenchmarkBatteryRuns.CountAsync(ct));
    }

    [Fact]
    public async Task Attach_ToAStoppedBatteryRun_Returns200_WithTheAttachedMemberInItsSlot()
    {
        var f = await CreateFixtureAsync();
        var ct = TestContext.Current.CancellationToken;
        var batteryRun = await SeedBatteryRunAsync(f, null, BenchmarkRunSeriesStatus.Stopped);
        var run = await SeedLooseRunAsync(f);

        var result = await f.Controller.AttachBatteryMember(
            batteryRun.Id, new BenchmarkBatteryAttachDto { SuiteIndex = 0, Round = 1, RunId = run.Id }, ct);

        var dto = Assert.IsType<BenchmarkBatteryRunDto>(Assert.IsType<OkObjectResult>(result).Value);
        var member = dto.Slots[0].Member!;
        Assert.Equal(run.Id, member.RunId);
        Assert.Equal("Attached", member.Origin);
        Assert.True(member.Usable);
        Assert.Null(dto.Slots[1].Member);
        Assert.Equal(1, dto.CompletedMemberCount);
        Assert.Equal("Stopped", dto.Status);
    }

    [Fact]
    public async Task Attach_RefusalsMapTo404_409_And400()
    {
        var f = await CreateFixtureAsync();
        var ct = TestContext.Current.CancellationToken;
        var stopped = await SeedBatteryRunAsync(f, null, BenchmarkRunSeriesStatus.Stopped);
        var occupant = await SeedMemberAsync(f, stopped, suiteIndex: 0);
        var running = await SeedBatteryRunAsync(f, null, BenchmarkRunSeriesStatus.Running);
        var run = await SeedLooseRunAsync(f);

        Assert.IsType<NotFoundObjectResult>(await f.Controller.AttachBatteryMember(
            4242, new BenchmarkBatteryAttachDto { SuiteIndex = 0, Round = 1, RunId = run.Id }, ct));
        Assert.IsType<NotFoundObjectResult>(await f.Controller.AttachBatteryMember(
            stopped.Id, new BenchmarkBatteryAttachDto { SuiteIndex = 1, Round = 1, RunId = 4242 }, ct));

        Assert.IsType<ConflictObjectResult>(await f.Controller.AttachBatteryMember(
            running.Id, new BenchmarkBatteryAttachDto { SuiteIndex = 0, Round = 1, RunId = run.Id }, ct));

        var occupied = Assert.IsType<BadRequestObjectResult>(await f.Controller.AttachBatteryMember(
            stopped.Id, new BenchmarkBatteryAttachDto { SuiteIndex = 0, Round = 1, RunId = run.Id }, ct));
        Assert.Contains($"run #{occupant.Id}", Assert.IsType<string>(occupied.Value));

        var otherSuite = Assert.IsType<BadRequestObjectResult>(await f.Controller.AttachBatteryMember(
            stopped.Id, new BenchmarkBatteryAttachDto { SuiteIndex = 1, Round = 1, RunId = run.Id }, ct));
        Assert.Contains("Suite B", Assert.IsType<string>(otherSuite.Value));

        Assert.IsType<BadRequestObjectResult>(await f.Controller.AttachBatteryMember(
            stopped.Id, new BenchmarkBatteryAttachDto { SuiteIndex = 0, Round = 2, RunId = run.Id }, ct));
    }

    [Fact]
    public async Task Candidates_Return200WithEligibility_404ForAnUnknownBatteryRun_And400OutsideTheGrid()
    {
        var f = await CreateFixtureAsync();
        var ct = TestContext.Current.CancellationToken;
        var batteryRun = await SeedBatteryRunAsync(f, null, BenchmarkRunSeriesStatus.Stopped);
        var eligible = await SeedLooseRunAsync(f);
        var withheld = await SeedLooseRunAsync(f, qualityIndex: null);

        var candidates = Assert.IsAssignableFrom<IReadOnlyList<BenchmarkBatteryAttachCandidateDto>>(
            Assert.IsType<OkObjectResult>(await f.Controller.GetBatteryAttachCandidates(batteryRun.Id, 0, 1, ct)).Value);
        Assert.Equal(2, candidates.Count);
        Assert.True(candidates.Single(c => c.RunId == eligible.Id).Eligible);
        var refused = candidates.Single(c => c.RunId == withheld.Id);
        Assert.False(refused.Eligible);
        Assert.Contains(BenchmarkBatteryPlanner.IndexWithheldReason, refused.Reason);

        Assert.IsType<NotFoundObjectResult>(await f.Controller.GetBatteryAttachCandidates(4242, 0, 1, ct));
        Assert.IsType<BadRequestObjectResult>(await f.Controller.GetBatteryAttachCandidates(batteryRun.Id, 2, 1, ct));
    }

    // --- Delete ----------------------------------------------------------------------------------

    [Fact]
    public async Task Delete_IsRefusedWhileABatteryRunIsActive_ThenKeepsTheRunWithANullReference()
    {
        var f = await CreateFixtureAsync();
        var ct = TestContext.Current.CancellationToken;
        var battery = ReadBattery(await f.Controller.CreateBattery(CreateRequest("Pair", f.SuiteA.Id, f.SuiteB.Id), ct));
        var batteryRun = await SeedBatteryRunAsync(f, battery.Id, BenchmarkRunSeriesStatus.Running);

        Assert.IsType<ConflictObjectResult>(await f.Controller.DeleteBattery(battery.Id, ct));
        Assert.Equal(1, await f.Db.BenchmarkBatteries.CountAsync(ct));

        batteryRun.Status = BenchmarkRunSeriesStatus.Stopped;
        await f.Db.SaveChangesAsync(ct);

        Assert.IsType<OkResult>(await f.Controller.DeleteBattery(battery.Id, ct));
        Assert.Equal(0, await f.Db.BenchmarkBatteries.CountAsync(ct));
        Assert.Equal(0, await f.Db.BenchmarkBatterySuites.CountAsync(ct));

        var kept = await f.Db.BenchmarkBatteryRuns.AsNoTracking().SingleAsync(ct);
        Assert.Null(kept.BenchmarkBatteryId);
        Assert.Equal("Pair", kept.BatteryName);
    }

    // --- Deleting a battery run ------------------------------------------------------------------------

    [Theory]
    [InlineData(BenchmarkRunSeriesStatus.Pending)]
    [InlineData(BenchmarkRunSeriesStatus.Running)]
    [InlineData(BenchmarkRunSeriesStatus.WaitingForCap)]
    public async Task DeleteBatteryRun_IsRefusedWhileLive_AndDeletesNothing(BenchmarkRunSeriesStatus status)
    {
        var f = await CreateFixtureAsync();
        var ct = TestContext.Current.CancellationToken;
        var batteryRun = await SeedBatteryRunAsync(f, null, status);
        var member = await SeedMemberAsync(f, batteryRun, suiteIndex: 0);

        Assert.IsType<ConflictObjectResult>(await f.Controller.DeleteBatteryRun(batteryRun.Id, deleteMembers: true, ct));

        Assert.True(await f.Db.BenchmarkBatteryRuns.AnyAsync(r => r.Id == batteryRun.Id, ct));
        Assert.True(await f.Db.BenchmarkBatteryRunMembers.AnyAsync(m => m.BenchmarkBatteryRunId == batteryRun.Id, ct));
        Assert.True(await f.Db.BenchmarkRuns.AnyAsync(r => r.Id == member.Id, ct));
    }

    [Fact]
    public async Task DeleteBatteryRun_OfAnUnknownBatteryRun_Returns404()
    {
        var f = await CreateFixtureAsync();
        Assert.IsType<NotFoundResult>(await f.Controller.DeleteBatteryRun(4242, deleteMembers: false, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteBatteryRun_WithoutMembers_RemovesItsAnalysesAndMemberRows_AndKeepsTheMemberRuns()
    {
        var f = await CreateFixtureAsync();
        var ct = TestContext.Current.CancellationToken;
        var batteryRun = await SeedBatteryRunAsync(f, null, BenchmarkRunSeriesStatus.Completed);
        var first = await SeedMemberAsync(f, batteryRun, suiteIndex: 0);
        var second = await SeedMemberAsync(f, batteryRun, suiteIndex: 1);
        await SeedAnalysisAsync(f, batteryRun, 70, new string('1', 64), DateTime.UtcNow.AddMinutes(-5));
        var other = await SeedBatteryRunAsync(f, null, BenchmarkRunSeriesStatus.Completed);
        await SeedAnalysisAsync(f, other, 65, new string('1', 64), DateTime.UtcNow);

        Assert.IsType<NoContentResult>(await f.Controller.DeleteBatteryRun(batteryRun.Id, deleteMembers: false, ct));

        Assert.False(await f.Db.BenchmarkBatteryRuns.AnyAsync(r => r.Id == batteryRun.Id, ct));
        Assert.False(await f.Db.BenchmarkBatteryAnalyses.AnyAsync(a => a.BenchmarkBatteryRunId == batteryRun.Id, ct));
        Assert.False(await f.Db.BenchmarkBatteryRunMembers.AnyAsync(m => m.BenchmarkBatteryRunId == batteryRun.Id, ct));

        var remaining = await f.Db.BenchmarkRuns.Select(r => r.Id).OrderBy(id => id).ToListAsync(ct);
        Assert.Equal(new[] { first.Id, second.Id }.OrderBy(id => id), remaining);
        Assert.True(await f.Db.BenchmarkBatteryAnalyses.AnyAsync(a => a.BenchmarkBatteryRunId == other.Id, ct));
    }

    [Fact]
    public async Task DeleteBatteryRun_RemovesItsBatteryCompletionDocuments_AndKeepsAnotherBatteryRunsDocuments()
    {
        var f = await CreateFixtureAsync();
        var ct = TestContext.Current.CancellationToken;
        var batteryRun = await SeedBatteryRunAsync(f, null, BenchmarkRunSeriesStatus.Completed);
        var other = await SeedBatteryRunAsync(f, null, BenchmarkRunSeriesStatus.Completed);
        f.Db.BenchmarkReportDocuments.AddRange(
            BatteryReportHarness.BatteryDocument(batteryRun.Id, BenchmarkReportAudience.ExecutiveSummary),
            BatteryReportHarness.BatteryDocument(batteryRun.Id, BenchmarkReportAudience.TechnicalReport),
            BatteryReportHarness.BatteryDocument(other.Id, BenchmarkReportAudience.ExecutiveSummary));
        await f.Db.SaveChangesAsync(ct);

        var options = (DbContextOptions<ApplicationDbContext>)Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions
            .GetService<Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptions>(f.Db);
        var charts = TestChartStores.Unconfigured();
        var services = new ServiceCollection();
        services.AddScoped(_ => new ApplicationDbContext(options));
        services.AddScoped(sp => new BenchmarkReportRenderService(
            sp.GetRequiredService<ApplicationDbContext>(), charts, NullLogger<BenchmarkReportRenderService>.Instance));
        await using var provider = services.BuildServiceProvider();
        var documents = new BenchmarkBatteryReportDocumentService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new BenchmarkReportPackJobManager(TimeSpan.FromMilliseconds(10)),
            NullLogger<BenchmarkBatteryReportDocumentService>.Instance);

        Assert.IsType<NoContentResult>(await f.Controller.DeleteBatteryRun(batteryRun.Id, deleteMembers: false, ct, documents));

        var subjects = await f.Db.BenchmarkReportDocuments.AsNoTracking().Select(d => d.SubjectKey).ToListAsync(ct);
        Assert.Equal(new[] { BenchmarkBatteryReportDocumentService.SubjectKeyOf(other.Id) }, subjects);
    }

    [Fact]
    public async Task DeleteBatteryRun_WithMembers_DeletesItsMemberRuns_ButKeepsARunServingAnotherBatteryRun()
    {
        var f = await CreateFixtureAsync();
        var ct = TestContext.Current.CancellationToken;
        var batteryRun = await SeedBatteryRunAsync(f, null, BenchmarkRunSeriesStatus.CompletedWithErrors);
        var own = await SeedMemberAsync(f, batteryRun, suiteIndex: 0);
        var shared = await SeedMemberAsync(f, batteryRun, suiteIndex: 1);
        var loose = await SeedLooseRunAsync(f);

        var other = await SeedBatteryRunAsync(f, null, BenchmarkRunSeriesStatus.Stopped);
        f.Db.BenchmarkBatteryRunMembers.Add(new BenchmarkBatteryRunMember
        {
            BenchmarkBatteryRunId = other.Id,
            BenchmarkRunId = shared.Id,
            SuiteIndex = 1,
            Round = 1,
            Origin = BenchmarkBatteryMemberOrigin.Attached
        });
        await f.Db.SaveChangesAsync(ct);

        Assert.IsType<NoContentResult>(await f.Controller.DeleteBatteryRun(batteryRun.Id, deleteMembers: true, ct));

        Assert.False(await f.Db.BenchmarkBatteryRuns.AnyAsync(r => r.Id == batteryRun.Id, ct));
        Assert.False(await f.Db.BenchmarkRuns.AnyAsync(r => r.Id == own.Id, ct));

        var remaining = await f.Db.BenchmarkRuns.Select(r => r.Id).OrderBy(id => id).ToListAsync(ct);
        Assert.Equal(new[] { shared.Id, loose.Id }.OrderBy(id => id), remaining);
        Assert.True(await f.Db.BenchmarkBatteryRunMembers.AnyAsync(
            m => m.BenchmarkBatteryRunId == other.Id && m.BenchmarkRunId == shared.Id, ct));
    }

    [Fact]
    public async Task DeleteBatteryRun_WithMembers_IsRefusedWhileAMemberRunIsInFlight()
    {
        var f = await CreateFixtureAsync();
        var ct = TestContext.Current.CancellationToken;
        var batteryRun = await SeedBatteryRunAsync(f, null, BenchmarkRunSeriesStatus.Stopped);
        var member = await SeedMemberAsync(f, batteryRun, suiteIndex: 0, status: BenchmarkRunStatus.Running, qualityIndex: null);

        using var cts = new CancellationTokenSource();
        Assert.True(f.RunManager.TryStart(member.Id, cts, out _));
        try
        {
            Assert.IsType<ConflictObjectResult>(await f.Controller.DeleteBatteryRun(batteryRun.Id, deleteMembers: true, ct));
            Assert.True(await f.Db.BenchmarkBatteryRuns.AnyAsync(r => r.Id == batteryRun.Id, ct));
            Assert.True(await f.Db.BenchmarkRuns.AnyAsync(r => r.Id == member.Id, ct));
        }
        finally
        {
            f.RunManager.Complete(member.Id);
        }
    }

    // --- Battery run projection --------------------------------------------------------------------

    [Fact]
    public async Task BatteryRun_StoppedWithAnEmptySlot_IsResumable_AndShowsItsGrid()
    {
        var f = await CreateFixtureAsync();
        var ct = TestContext.Current.CancellationToken;
        var batteryRun = await SeedBatteryRunAsync(f, null, BenchmarkRunSeriesStatus.Stopped);
        var run = await SeedMemberAsync(f, batteryRun, suiteIndex: 0);

        var dto = Assert.IsType<BenchmarkBatteryRunDto>(
            Assert.IsType<OkObjectResult>(await f.Controller.GetBatteryRun(batteryRun.Id, ct)).Value);

        Assert.True(dto.Resumable);
        Assert.Equal("Stopped", dto.Status);
        Assert.Equal("A member run failed", dto.StopReasonText);
        Assert.Equal(2, dto.SuiteCount);
        Assert.Equal(1, dto.CompletedSuiteCount);
        Assert.Equal("Opus Under Test", dto.TestedModelLabel);

        Assert.Equal(2, dto.Slots.Count);
        Assert.Equal(run.Id, dto.Slots[0].Member!.RunId);
        Assert.True(dto.Slots[0].Member!.Usable);
        Assert.Null(dto.Slots[1].Member);
        Assert.Null(dto.CurrentSuiteIndex);
    }

    [Fact]
    public async Task BatteryRun_MemberWithItsIndexWithheld_IsNotUsable_AndNamesTheReason()
    {
        var f = await CreateFixtureAsync();
        var ct = TestContext.Current.CancellationToken;
        var batteryRun = await SeedBatteryRunAsync(f, null, BenchmarkRunSeriesStatus.CompletedWithErrors);
        await SeedMemberAsync(f, batteryRun, suiteIndex: 0);
        await SeedMemberAsync(f, batteryRun, suiteIndex: 1, status: BenchmarkRunStatus.CompletedWithErrors, qualityIndex: null);

        var dto = Assert.IsType<BenchmarkBatteryRunDto>(
            Assert.IsType<OkObjectResult>(await f.Controller.GetBatteryRun(batteryRun.Id, ct)).Value);

        var withheld = dto.Slots[1].Member!;
        Assert.False(withheld.Usable);
        Assert.Equal(BenchmarkBatteryPlanner.IndexWithheldReason, withheld.UnusableReason);
        Assert.True(dto.Resumable);
        Assert.Equal(1, dto.CompletedSuiteCount);
    }

    private static async Task SetAddedAtAsync(Fixture fixture, BenchmarkRun run, DateTime addedAtUtc, bool superseded = false)
    {
        var row = await fixture.Db.BenchmarkBatteryRunMembers.SingleAsync(m => m.BenchmarkRunId == run.Id);
        row.AddedAtUtc = addedAtUtc;
        row.Superseded = superseded;
        await fixture.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task BatteryRun_TakesItsIdentityFromTheNewestUsableMember_AndCarriesItsReportFields()
    {
        var f = await CreateFixtureAsync();
        var ct = TestContext.Current.CancellationToken;
        var now = DateTime.UtcNow;
        var batteryRun = await SeedBatteryRunAsync(f, null, BenchmarkRunSeriesStatus.Completed);
        batteryRun.ReportWriterModelConfigurationId = 9;
        batteryRun.ReportDocumentsStatus = BenchmarkRunReportDocumentsStatus.Failed;
        batteryRun.ReportDocumentsMessage = "The writer refused.";
        await f.Db.SaveChangesAsync(ct);

        var older = await SeedMemberAsync(f, batteryRun, suiteIndex: 0);
        await SetAddedAtAsync(f, older, now.AddMinutes(-10));

        var newest = await SeedMemberAsync(f, batteryRun, suiteIndex: 1);
        newest.TestedModelSnapshot = BenchmarkModelSnapshots.Model(
            provider: "OpenAI", modelId: "gpt-6", displayName: "GPT 6",
            thinkingLevel: "high", reasoningMode: "enabled", serviceTier: "flex");
        newest.AssessorModelSnapshot = BenchmarkModelSnapshots.Model("Google", "gemini-3.7-pro", "Gemini Grader", thinkingLevel: "medium");
        newest.CoAssessorModelSnapshot = BenchmarkModelSnapshots.Model(
            "Anthropic", "claude-co-reader", "Co Reader", thinkingLevel: "low", reasoningMode: "enabled");
        newest.ScoringProfile = new BenchmarkScoringProfile { Name = "Strict" };
        newest.CandidatePromptOptionsJson = "{\"verboseMode\":true}";
        await f.Db.SaveChangesAsync(ct);
        await SetAddedAtAsync(f, newest, now.AddMinutes(-5));

        // Newer still, but superseded: never the identity while a usable member exists.
        var superseded = await SeedMemberAsync(f, batteryRun, suiteIndex: 0);
        superseded.TestedModelSnapshot = BenchmarkModelSnapshots.Model("Google", "superseded-model");
        await f.Db.SaveChangesAsync(ct);
        await SetAddedAtAsync(f, superseded, now, superseded: true);

        var dto = Assert.IsType<BenchmarkBatteryRunDto>(
            Assert.IsType<OkObjectResult>(await f.Controller.GetBatteryRun(batteryRun.Id, ct)).Value);

        Assert.Equal("OpenAI", dto.TestedProvider);
        Assert.Equal("gpt-6", dto.TestedModelId);
        Assert.Equal("high", dto.TestedThinkingLevel);
        Assert.Equal("enabled", dto.TestedReasoningMode);
        Assert.Equal("flex", dto.TestedServiceTier);
        Assert.Equal("Gemini Grader", dto.AssessorLabel);
        Assert.Equal("Google", dto.AssessorProvider);
        Assert.Equal("medium", dto.AssessorThinkingLevel);
        Assert.Equal("Co Reader", dto.CoAssessorLabel);
        Assert.Equal("Anthropic", dto.CoAssessorProvider);
        Assert.Equal("low", dto.CoAssessorThinkingLevel);
        Assert.Equal("enabled", dto.CoAssessorReasoningMode);
        Assert.Equal("Strict", dto.ScoringProfileName);
        Assert.True(dto.VerboseMode);

        Assert.Equal((long?)9, dto.ReportWriterModelConfigurationId);
        Assert.Equal(BenchmarkRunReportDocumentsStatus.Failed, dto.ReportDocumentsStatus);
        Assert.Equal("The writer refused.", dto.ReportDocumentsMessage);
    }

    [Fact]
    public async Task BatteryRun_WithoutMembers_TakesItsIdentityFromTheStartRequestsConfigurations()
    {
        var f = await CreateFixtureAsync();
        var ct = TestContext.Current.CancellationToken;
        f.Db.SystemAiApiConfigurations.AddRange(
            new SystemAiApiConfiguration
            {
                Id = 1,
                DisplayName = "Opus Configured",
                Provider = "Anthropic",
                ModelId = "claude-opus-5",
                ThinkingLevel = "max",
                ServiceTier = "priority"
            },
            new SystemAiApiConfiguration { Id = 2, DisplayName = "Assessor Configured", Provider = "Google", ModelId = "gemini-3.7-pro" });
        await f.Db.SaveChangesAsync(ct);
        var batteryRun = await SeedBatteryRunAsync(f, null, BenchmarkRunSeriesStatus.Pending);

        var dto = Assert.IsType<BenchmarkBatteryRunDto>(
            Assert.IsType<OkObjectResult>(await f.Controller.GetBatteryRun(batteryRun.Id, ct)).Value);

        Assert.Equal("Anthropic", dto.TestedProvider);
        Assert.Equal("claude-opus-5", dto.TestedModelId);
        Assert.Equal("max", dto.TestedThinkingLevel);
        Assert.Null(dto.TestedReasoningMode);
        Assert.Equal("priority", dto.TestedServiceTier);
        Assert.Equal("Assessor Configured", dto.AssessorLabel);
        Assert.Equal("Google", dto.AssessorProvider);
        Assert.Null(dto.AssessorThinkingLevel);
        Assert.Null(dto.CoAssessorLabel);
        Assert.Null(dto.CoAssessorProvider);
        Assert.Null(dto.ScoringProfileName);
        Assert.False(dto.VerboseMode);
        Assert.Null(dto.ReportWriterModelConfigurationId);
        Assert.Equal(BenchmarkRunReportDocumentsStatus.NotRequested, dto.ReportDocumentsStatus);
        Assert.Null(dto.ReportWriterDisplayName);
        Assert.Equal(0, dto.ReportDocumentsWrittenCount);
    }

    [Fact]
    public async Task BatteryRun_CarriesItsReportWritersSettings_AndCountsItsWrittenDocuments()
    {
        var f = await CreateFixtureAsync();
        var ct = TestContext.Current.CancellationToken;
        f.Db.SystemAiApiConfigurations.Add(new SystemAiApiConfiguration
        {
            Id = 9,
            DisplayName = "Writer Configured",
            Provider = "Anthropic",
            ModelId = "claude-writer",
            ThinkingLevel = "high",
            ReasoningMode = "adaptive",
            ServiceTier = "priority"
        });
        await f.Db.SaveChangesAsync(ct);

        var batteryRun = await SeedBatteryRunAsync(f, null, BenchmarkRunSeriesStatus.Completed);
        batteryRun.ReportWriterModelConfigurationId = 9;
        var orphaned = await SeedBatteryRunAsync(f, null, BenchmarkRunSeriesStatus.Completed);
        orphaned.ReportWriterModelConfigurationId = 99;
        f.Db.BenchmarkReportDocuments.AddRange(
            BatteryReportHarness.BatteryDocument(batteryRun.Id, BenchmarkReportAudience.ExecutiveSummary),
            BatteryReportHarness.BatteryDocument(batteryRun.Id, BenchmarkReportAudience.TechnicalReport),
            BatteryReportHarness.BatteryDocument(orphaned.Id, BenchmarkReportAudience.ExecutiveSummary));
        await f.Db.SaveChangesAsync(ct);

        var dto = Assert.IsType<BenchmarkBatteryRunDto>(
            Assert.IsType<OkObjectResult>(await f.Controller.GetBatteryRun(batteryRun.Id, ct)).Value);

        Assert.Equal("Writer Configured", dto.ReportWriterDisplayName);
        Assert.Equal("Anthropic", dto.ReportWriterProvider);
        Assert.Equal("claude-writer", dto.ReportWriterModelId);
        Assert.Equal("high", dto.ReportWriterThinkingLevel);
        Assert.Equal("adaptive", dto.ReportWriterReasoningMode);
        Assert.Equal("priority", dto.ReportWriterServiceTier);
        Assert.Equal(2, dto.ReportDocumentsWrittenCount);

        // A writer whose configuration has been deleted keeps its id and loses its settings.
        var gone = Assert.IsType<BenchmarkBatteryRunDto>(
            Assert.IsType<OkObjectResult>(await f.Controller.GetBatteryRun(orphaned.Id, ct)).Value);

        Assert.Equal((long?)99, gone.ReportWriterModelConfigurationId);
        Assert.Null(gone.ReportWriterDisplayName);
        Assert.Null(gone.ReportWriterProvider);
        Assert.Null(gone.ReportWriterModelId);
        Assert.Null(gone.ReportWriterThinkingLevel);
        Assert.Null(gone.ReportWriterReasoningMode);
        Assert.Null(gone.ReportWriterServiceTier);
        Assert.Equal(1, gone.ReportDocumentsWrittenCount);
    }

    [Fact]
    public async Task BatteryRun_Members_CarryTheirStageProgressHalfWidthDurationAndCounts()
    {
        var f = await CreateFixtureAsync();
        var ct = TestContext.Current.CancellationToken;
        var batteryRun = await SeedBatteryRunAsync(f, null, BenchmarkRunSeriesStatus.Running);

        var finished = await SeedMemberAsync(f, batteryRun, suiteIndex: 0);
        finished.AnsweredQuestionCount = 1;
        finished.QualityIndexStandardError = 2.5;
        finished.CompletedAtUtc = finished.StartedAtUtc.AddMinutes(13).AddSeconds(40);
        finished.ClaimsRefutedCount = 2;
        finished.AdvisoryFlagAnswerCount = 3;

        var running = await SeedMemberAsync(f, batteryRun, suiteIndex: 1, status: BenchmarkRunStatus.Running, qualityIndex: null);
        running.TotalQuestionCount = 2;
        running.QualityIndexStandardError = 4.0;
        running.Answers.Add(new BenchmarkRunAnswer
        {
            OrderIndex = 1,
            QuestionText = "Suite B Q1",
            AnswerText = "An answer.",
            Status = BenchmarkAnswerStatus.Ok
        });
        await f.Db.SaveChangesAsync(ct);

        using var cts = new CancellationTokenSource();
        Assert.True(f.RunManager.TryStart(running.Id, cts, out _));
        try
        {
            f.RunManager.MarkStage(running.Id, BenchmarkRunStage.Verifying);

            var dto = Assert.IsType<BenchmarkBatteryRunDto>(
                Assert.IsType<OkObjectResult>(await f.Controller.GetBatteryRun(batteryRun.Id, ct)).Value);

            // Finished: the run's own answered count, 1.96 × SE, 13 min 40 s, and its counts.
            var done = dto.Members.Single(m => m.RunId == finished.Id);
            Assert.Null(done.Stage);
            Assert.Equal(1, done.AnsweredQuestionCount);
            Assert.Equal(1.96 * 2.5, done.QualityIndexHalfWidth!.Value, 9);
            Assert.Equal(820_000L, done.DurationMs);
            Assert.Equal(2, done.ClaimsRefutedCount);
            Assert.Equal(3, done.AdvisoryFlagAnswerCount);

            // Running: the run manager's stage and the answer rows so far; no index, so no half-width.
            var live = dto.Members.Single(m => m.RunId == running.Id);
            Assert.Equal("Verifying", live.Stage);
            Assert.Equal(1, live.AnsweredQuestionCount);
            Assert.Equal(2, live.TotalQuestionCount);
            Assert.Null(live.QualityIndexHalfWidth);
            Assert.Null(live.DurationMs);
        }
        finally
        {
            f.RunManager.Complete(running.Id);
        }

        // Once this process no longer drives it, a member still marked running carries no stage.
        var after = Assert.IsType<BenchmarkBatteryRunDto>(
            Assert.IsType<OkObjectResult>(await f.Controller.GetBatteryRun(batteryRun.Id, ct)).Value);
        Assert.Null(after.Members.Single(m => m.RunId == running.Id).Stage);
    }

    // --- Live cost and mean model time -------------------------------------------------------------

    private static readonly ModelPricing LiveCandidateCard = new(10.00m, 50.00m);
    private static readonly ModelPricing LiveAssessorCard = new(3.00m, 15.00m);

    /// <summary>A pricing service that returns one fixed card set for every run.</summary>
    private sealed class FixedPricingService : ModelPricingService
    {
        private readonly BenchmarkRunPricing _pricing;

        public FixedPricingService(ApplicationDbContext db, BenchmarkRunPricing pricing)
            : base(new ModelMetadataService(), db)
        {
            _pricing = pricing;
        }

        public override Task<BenchmarkRunPricing> ResolveForRunAsync(BenchmarkRun run) => Task.FromResult(_pricing);
    }

    private static readonly ModelPricing LiveVerifierCard = new(2.00m, 8.00m);

    /// <summary>A pricing service that picks each run's card set by the run.</summary>
    private sealed class PerRunPricingService : ModelPricingService
    {
        private readonly Func<BenchmarkRun, BenchmarkRunPricing> _pricing;

        public PerRunPricingService(ApplicationDbContext db, Func<BenchmarkRun, BenchmarkRunPricing> pricing)
            : base(new ModelMetadataService(), db)
        {
            _pricing = pricing;
        }

        public override Task<BenchmarkRunPricing> ResolveForRunAsync(BenchmarkRun run) => Task.FromResult(_pricing(run));
    }

    private static BenchmarkRunAnswer TimedAnswer(
        int orderIndex,
        long durationMs,
        long? toolTimeMs,
        BenchmarkAnswerStatus status = BenchmarkAnswerStatus.Ok) => new()
    {
        OrderIndex = orderIndex,
        QuestionText = $"Q{orderIndex}",
        AnswerText = "An answer.",
        Status = status,
        DurationMs = durationMs,
        ToolTimeMs = toolTimeMs
    };

    [Fact]
    public async Task BatteryRun_LiveFigures_SumTheMembersCostByRole_PoolTheirModelTime_AndAddTheReportWriterCost()
    {
        var f = await CreateFixtureAsync();
        var ct = TestContext.Current.CancellationToken;
        var batteryRun = await SeedBatteryRunAsync(f, null, BenchmarkRunSeriesStatus.Running);
        var otherBatteryRun = await SeedBatteryRunAsync(f, null, BenchmarkRunSeriesStatus.Completed);

        // Finished: costed from its finalized columns — candidate $10 + $5, assessor $3.
        var finished = await SeedMemberAsync(f, batteryRun, suiteIndex: 0);
        finished.TotalInputTokens = 1_000_000;
        finished.TotalOutputTokens = 100_000;
        finished.TotalAssessmentInputTokens = 1_000_000;
        finished.Answers.Add(TimedAnswer(1, 10_000, 4_000));     // 6,000 ms
        finished.Answers.Add(TimedAnswer(2, 2_000, null));       // 2,000 ms

        // Running: its columns are still zero, so it is costed from its answer rows — candidate
        // $5 + $1, assessor $3.
        var running = await SeedMemberAsync(f, batteryRun, suiteIndex: 1, status: BenchmarkRunStatus.Running, qualityIndex: null);
        var costed = TimedAnswer(1, 9_000, 1_000);               // 8,000 ms
        costed.InputTokens = 500_000;
        costed.OutputTokens = 20_000;
        costed.AssessmentInputTokens = 1_000_000;
        running.Answers.Add(costed);
        running.Answers.Add(TimedAnswer(2, 1_000, 3_000));       // floored at 0 ms
        running.Answers.Add(TimedAnswer(3, 7_000, 0));           // 7,000 ms
        running.Answers.Add(TimedAnswer(4, 50_000, null, BenchmarkAnswerStatus.ProviderError));

        // Superseded: neither its cost nor its time counts.
        var superseded = await SeedMemberAsync(f, batteryRun, suiteIndex: 0);
        superseded.TotalInputTokens = 2_000_000;
        superseded.Answers.Add(TimedAnswer(1, 100_000, null));
        await f.Db.SaveChangesAsync(ct);
        await SetAddedAtAsync(f, superseded, DateTime.UtcNow, superseded: true);

        var executive = BatteryReportHarness.BatteryDocument(batteryRun.Id, BenchmarkReportAudience.ExecutiveSummary);
        executive.CostUsd = 0.40m;
        var technical = BatteryReportHarness.BatteryDocument(batteryRun.Id, BenchmarkReportAudience.TechnicalReport);
        technical.CostUsd = 0.35m;
        var otherDocument = BatteryReportHarness.BatteryDocument(otherBatteryRun.Id, BenchmarkReportAudience.ExecutiveSummary);
        otherDocument.CostUsd = 9.00m;
        f.Db.BenchmarkReportDocuments.AddRange(executive, technical, otherDocument);
        await f.Db.SaveChangesAsync(ct);

        var estimator = new BenchmarkRunCostEstimator(new FixedPricingService(
            f.Db,
            new BenchmarkRunPricing(Candidate: LiveCandidateCard, Assessor: LiveAssessorCard, ClaimVerifier: null, SecondOpinion: null)));

        var dto = Assert.IsType<BenchmarkBatteryRunDto>(
            Assert.IsType<OkObjectResult>(await f.Controller.GetBatteryRun(batteryRun.Id, ct, estimator)).Value);

        var done = dto.Members.Single(m => m.RunId == finished.Id);
        Assert.Equal(18.00m, done.EstimatedCost);
        Assert.Equal(15.00m, done.EstimatedCandidateCost);
        Assert.Equal(4_000.0, done.MeanModelTimeMs!.Value, 9);

        var live = dto.Members.Single(m => m.RunId == running.Id);
        Assert.Equal(9.00m, live.EstimatedCost);
        Assert.Equal(6.00m, live.EstimatedCandidateCost);
        Assert.Equal(5_000.0, live.MeanModelTimeMs!.Value, 9);

        var old = dto.Members.Single(m => m.RunId == superseded.Id);
        Assert.Null(old.EstimatedCost);
        Assert.Null(old.EstimatedCandidateCost);
        Assert.Null(old.MeanModelTimeMs);

        // Pooled over the five Ok answers of the two current members, not the mean of their means.
        Assert.Equal(5, dto.ModelTimedAnswerCount);
        Assert.Equal(23_000.0 / 5, dto.MeanModelTimeMs!.Value, 9);

        var cost = Assert.IsType<BenchmarkBatteryLiveCostDto>(dto.LiveCost);
        Assert.Equal(27.00m, cost.Total);
        Assert.Equal(21.00m, cost.Candidate);
        Assert.Equal(6.00m, cost.Assessor);
        Assert.Equal(6.00m, cost.Grading);

        // A role no member spent anything on has no figure, rather than zero.
        Assert.Null(cost.SecondOpinion);
        Assert.Null(cost.ClaimVerifier);
        Assert.Null(cost.Synthesis);
        Assert.Null(cost.CoAssessor);
        Assert.Null(cost.CoSynthesis);

        Assert.False(cost.PricingIncomplete);
        Assert.Equal("catalog", cost.PricingSource);
        Assert.Equal(2, cost.PricedMemberCount);
        Assert.Equal(0.75m, cost.ReportWriterCostUsd);

        // Without an estimator the live cost is absent; the model time does not need one.
        var unpriced = Assert.IsType<BenchmarkBatteryRunDto>(
            Assert.IsType<OkObjectResult>(await f.Controller.GetBatteryRun(batteryRun.Id, ct)).Value);
        Assert.Null(unpriced.LiveCost);
        Assert.Equal(5, unpriced.ModelTimedAnswerCount);

        // The battery-run list carries none of the live figures.
        var listed = Assert.IsType<List<BenchmarkBatteryRunDto>>(
            Assert.IsType<OkObjectResult>(await f.Controller.GetBatteryRuns(null, null, ct)).Value)
            .Single(r => r.Id == batteryRun.Id);
        Assert.Null(listed.LiveCost);
        Assert.Null(listed.MeanModelTimeMs);
        Assert.Equal(0, listed.ModelTimedAnswerCount);
        Assert.All(listed.Members, m => Assert.Null(m.EstimatedCost));
    }

    [Fact]
    public async Task BatteryRun_LiveCost_IsIncomplete_AndHasNoTotal_WhenAMembersRoleHasNoCard()
    {
        var f = await CreateFixtureAsync();
        var ct = TestContext.Current.CancellationToken;
        var batteryRun = await SeedBatteryRunAsync(f, null, BenchmarkRunSeriesStatus.Completed);

        var priced = await SeedMemberAsync(f, batteryRun, suiteIndex: 0);
        priced.TotalInputTokens = 1_000_000;

        // Its second reader spent tokens against no card.
        var unpriced = await SeedMemberAsync(f, batteryRun, suiteIndex: 1);
        unpriced.TotalInputTokens = 1_000_000;
        unpriced.TotalSecondOpinionInputTokens = 1_000_000;
        await f.Db.SaveChangesAsync(ct);

        var estimator = new BenchmarkRunCostEstimator(new FixedPricingService(
            f.Db,
            new BenchmarkRunPricing(Candidate: LiveCandidateCard, Assessor: LiveAssessorCard, ClaimVerifier: null, SecondOpinion: null)));

        var dto = Assert.IsType<BenchmarkBatteryRunDto>(
            Assert.IsType<OkObjectResult>(await f.Controller.GetBatteryRun(batteryRun.Id, ct, estimator)).Value);

        var cost = Assert.IsType<BenchmarkBatteryLiveCostDto>(dto.LiveCost);
        Assert.True(cost.PricingIncomplete);
        Assert.Null(cost.Total);
        Assert.Equal(20.00m, cost.Candidate);
        Assert.Null(cost.ReportWriterCostUsd);
        Assert.Null(dto.Members.Single(m => m.RunId == unpriced.Id).EstimatedCost);
        Assert.Equal(10.00m, dto.Members.Single(m => m.RunId == priced.Id).EstimatedCost);

        // No Ok answer anywhere: no mean, and nothing counted.
        Assert.Null(dto.MeanModelTimeMs);
        Assert.Equal(0, dto.ModelTimedAnswerCount);
    }

    [Fact]
    public async Task BatteryRun_LiveCost_KeepsARoleTheRunningMemberHasNotSpentOnYet()
    {
        var f = await CreateFixtureAsync();
        var ct = TestContext.Current.CancellationToken;
        var batteryRun = await SeedBatteryRunAsync(f, null, BenchmarkRunSeriesStatus.Running);

        // Finished: candidate $10, assessor $3, claim verifier $2, synthesis $3 on the assessor's card.
        var finished = await SeedMemberAsync(f, batteryRun, suiteIndex: 0);
        finished.TotalInputTokens = 1_000_000;
        finished.TotalAssessmentInputTokens = 1_000_000;
        finished.TotalClaimVerificationInputTokens = 1_000_000;
        finished.TotalSynthesisInputTokens = 1_000_000;

        // Running: candidate $5 and assessor $3 so far; no verification or synthesis yet.
        var running = await SeedMemberAsync(f, batteryRun, suiteIndex: 1, status: BenchmarkRunStatus.Running, qualityIndex: null);
        var costed = TimedAnswer(1, 9_000, 1_000);
        costed.InputTokens = 500_000;
        costed.AssessmentInputTokens = 1_000_000;
        running.Answers.Add(costed);
        await f.Db.SaveChangesAsync(ct);

        var estimator = new BenchmarkRunCostEstimator(new FixedPricingService(
            f.Db,
            new BenchmarkRunPricing(Candidate: LiveCandidateCard, Assessor: LiveAssessorCard, ClaimVerifier: LiveVerifierCard, SecondOpinion: null)));

        var dto = Assert.IsType<BenchmarkBatteryRunDto>(
            Assert.IsType<OkObjectResult>(await f.Controller.GetBatteryRun(batteryRun.Id, ct, estimator)).Value);

        var cost = Assert.IsType<BenchmarkBatteryLiveCostDto>(dto.LiveCost);
        Assert.Equal(2.00m, cost.ClaimVerifier);
        Assert.Equal(3.00m, cost.Synthesis);
        Assert.Equal(6.00m, cost.Assessor);
        Assert.Equal(15.00m, cost.Candidate);
        Assert.Equal(11.00m, cost.Grading);
        Assert.Equal(26.00m, cost.Total);
        Assert.Equal(cost.Candidate + cost.Grading, cost.Total);
        Assert.False(cost.PricingIncomplete);

        Assert.Null(cost.SecondOpinion);
        Assert.Null(cost.CoAssessor);
        Assert.Null(cost.CoSynthesis);
    }

    [Fact]
    public async Task BatteryRun_LiveCost_RoleIsNull_WhenAMemberSpentOnItWithoutACard()
    {
        var f = await CreateFixtureAsync();
        var ct = TestContext.Current.CancellationToken;
        var batteryRun = await SeedBatteryRunAsync(f, null, BenchmarkRunSeriesStatus.Completed);

        // Both members' verifiers spent tokens; only the first one's has a card.
        var priced = await SeedMemberAsync(f, batteryRun, suiteIndex: 0);
        priced.TotalInputTokens = 1_000_000;
        priced.TotalClaimVerificationInputTokens = 1_000_000;

        var unpriced = await SeedMemberAsync(f, batteryRun, suiteIndex: 1);
        unpriced.TotalInputTokens = 1_000_000;
        unpriced.TotalClaimVerificationInputTokens = 1_000_000;
        await f.Db.SaveChangesAsync(ct);

        var estimator = new BenchmarkRunCostEstimator(new PerRunPricingService(
            f.Db,
            run => new BenchmarkRunPricing(
                Candidate: LiveCandidateCard,
                Assessor: LiveAssessorCard,
                ClaimVerifier: run.Id == priced.Id ? LiveVerifierCard : null,
                SecondOpinion: null)));

        var dto = Assert.IsType<BenchmarkBatteryRunDto>(
            Assert.IsType<OkObjectResult>(await f.Controller.GetBatteryRun(batteryRun.Id, ct, estimator)).Value);

        var cost = Assert.IsType<BenchmarkBatteryLiveCostDto>(dto.LiveCost);
        Assert.Null(cost.ClaimVerifier);
        Assert.True(cost.PricingIncomplete);
        Assert.Null(cost.Total);
        Assert.Equal(20.00m, cost.Candidate);
    }

    [Fact]
    public async Task BatteryRunList_ReturnsTheNewest50ByDefault_AndUpTo1000WhenAsked()
    {
        Assert.Equal(50, AdminBenchmarkBatteriesController.DefaultRunListSize);
        Assert.Equal(1000, AdminBenchmarkBatteriesController.MaxRunListSize);

        var f = await CreateFixtureAsync();
        var ct = TestContext.Current.CancellationToken;
        for (int i = 0; i < 201; i++)
        {
            await SeedBatteryRunAsync(f, null, BenchmarkRunSeriesStatus.Completed);
        }

        List<BenchmarkBatteryRunDto> ReadRuns(IActionResult result)
            => Assert.IsAssignableFrom<IEnumerable<BenchmarkBatteryRunDto>>(Assert.IsType<OkObjectResult>(result).Value).ToList();

        Assert.Equal(50, ReadRuns(await f.Controller.GetBatteryRuns(null, null, ct)).Count);
        Assert.Equal(201, ReadRuns(await f.Controller.GetBatteryRuns(null, 5000, ct)).Count);
        Assert.Single(ReadRuns(await f.Controller.GetBatteryRuns(null, 0, ct)));
    }

    // --- Leaderboard -------------------------------------------------------------------------------

    private static async Task SeedAnalysisAsync(
        Fixture fixture,
        BenchmarkBatteryRun batteryRun,
        double? overallIndex,
        string? comparabilityClass,
        DateTime computedAtUtc)
    {
        bool complete = overallIndex.HasValue;
        var result = new BenchmarkBatteryStatisticsResult
        {
            Complete = complete,
            SuiteCount = 2,
            CompletedSuiteCount = complete ? 2 : 1,
            OverallIndex = complete ? new BenchmarkBatteryOverallIndex { PointEstimate = overallIndex!.Value } : null
        };

        fixture.Db.BenchmarkBatteryAnalyses.Add(new BenchmarkBatteryAnalysis
        {
            BenchmarkBatteryRunId = batteryRun.Id,
            ComputedAtUtc = computedAtUtc,
            MemberRunIdsJson = "[]",
            ResultJson = JsonSerializer.Serialize(result),
            DefinitionSha256 = batteryRun.DefinitionSha256,
            ComparabilityClassSha256 = complete ? comparabilityClass : null,
            Complete = complete,
            HarnessVersion = "30",
            ScoringMethodVersion = 12
        });
        await fixture.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Leaderboard_ReadsTheLatestAnalysisPerRunWithTheHash_AndRanksWithinEachClass()
    {
        var f = await CreateFixtureAsync();
        var ct = TestContext.Current.CancellationToken;
        string hash = new string('a', 64);
        string otherHash = new string('b', 64);
        string classOne = new string('1', 64);
        string classTwo = new string('2', 64);
        var now = DateTime.UtcNow;

        var r1 = await SeedBatteryRunAsync(f, null, BenchmarkRunSeriesStatus.Completed, hash);
        var r2 = await SeedBatteryRunAsync(f, null, BenchmarkRunSeriesStatus.Completed, hash);
        var r3 = await SeedBatteryRunAsync(f, null, BenchmarkRunSeriesStatus.Completed, hash);
        var r4 = await SeedBatteryRunAsync(f, null, BenchmarkRunSeriesStatus.Stopped, hash);
        var r5 = await SeedBatteryRunAsync(f, null, BenchmarkRunSeriesStatus.Completed, otherHash);

        await SeedAnalysisAsync(f, r1, 10, classOne, now.AddHours(-3));
        await SeedAnalysisAsync(f, r1, 60, classOne, now.AddHours(-1));
        await SeedAnalysisAsync(f, r2, 75, classOne, now.AddHours(-2));
        await SeedAnalysisAsync(f, r3, 80, classTwo, now.AddMinutes(-30));
        await SeedAnalysisAsync(f, r4, null, null, now.AddMinutes(-20));
        await SeedAnalysisAsync(f, r5, 99, classOne, now);

        var board = Assert.IsType<BenchmarkBatteryLeaderboardDto>(
            Assert.IsType<OkObjectResult>(await f.Controller.GetLeaderboard(hash.ToUpperInvariant(), ct)).Value);

        Assert.Equal(hash, board.DefinitionSha256);
        Assert.Equal(2, board.Classes.Count);

        var one = board.Classes.Single(c => c.ComparabilityClassSha256 == classOne);
        Assert.Equal(new[] { r2.Id, r1.Id }, one.Rows.Select(r => r.BatteryRunId));
        Assert.Equal(60.0, one.Rows.Single(r => r.BatteryRunId == r1.Id).OverallIndex!.Value, 6);
        Assert.StartsWith("Harness 30", one.Label);

        var two = board.Classes.Single(c => c.ComparabilityClassSha256 == classTwo);
        Assert.Equal(new[] { r3.Id }, two.Rows.Select(r => r.BatteryRunId));

        var incomplete = Assert.Single(board.Incomplete);
        Assert.Equal(r4.Id, incomplete.BatteryRunId);
        Assert.False(incomplete.Complete);

        Assert.DoesNotContain(board.Classes.SelectMany(c => c.Rows), r => r.BatteryRunId == r5.Id);
    }

    [Fact]
    public async Task Leaderboard_WithoutAHash_Returns400()
    {
        var f = await CreateFixtureAsync();
        Assert.IsType<BadRequestObjectResult>(await f.Controller.GetLeaderboard(" ", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LeaderboardRows_CarryTheTestedModelsIdentity()
    {
        var f = await CreateFixtureAsync();
        var ct = TestContext.Current.CancellationToken;
        string hash = new string('a', 64);
        var batteryRun = await SeedBatteryRunAsync(f, null, BenchmarkRunSeriesStatus.Completed, hash);
        var member = await SeedMemberAsync(f, batteryRun, suiteIndex: 0);
        member.TestedModelSnapshot = BenchmarkModelSnapshots.Model(
            provider: "Anthropic", modelId: "claude-opus-5", displayName: "Opus Under Test",
            thinkingLevel: "high", reasoningMode: "adaptive", serviceTier: "standard");
        await f.Db.SaveChangesAsync(ct);
        await SeedAnalysisAsync(f, batteryRun, 72, new string('1', 64), DateTime.UtcNow);

        var board = Assert.IsType<BenchmarkBatteryLeaderboardDto>(
            Assert.IsType<OkObjectResult>(await f.Controller.GetLeaderboard(hash, ct)).Value);

        var row = Assert.Single(Assert.Single(board.Classes).Rows);
        Assert.Equal("Anthropic", row.TestedProvider);
        Assert.Equal("claude-opus-5", row.TestedModelId);
        Assert.Equal("high", row.TestedThinkingLevel);
        Assert.Equal("adaptive", row.TestedReasoningMode);
        Assert.Equal("standard", row.TestedServiceTier);
    }

    [Fact]
    public async Task BatteryList_CountsTheRankedResultsOfTheCurrentDefinition_AsTheLeaderboardRanksThem()
    {
        var f = await CreateFixtureAsync();
        var ct = TestContext.Current.CancellationToken;
        var created = ReadBattery(await f.Controller.CreateBattery(CreateRequest("Pair", f.SuiteA.Id, f.SuiteB.Id), ct));
        string hash = created.DefinitionSha256;
        string classOne = new string('1', 64);
        string classTwo = new string('2', 64);
        var now = DateTime.UtcNow;

        Assert.Equal(0, created.RankedResultCount);
        Assert.Null(created.LatestAnalysisAtUtc);

        var r1 = await SeedBatteryRunAsync(f, created.Id, BenchmarkRunSeriesStatus.Completed, hash);
        var r2 = await SeedBatteryRunAsync(f, created.Id, BenchmarkRunSeriesStatus.Completed, hash);
        var r3 = await SeedBatteryRunAsync(f, null, BenchmarkRunSeriesStatus.Completed, hash);
        var r4 = await SeedBatteryRunAsync(f, created.Id, BenchmarkRunSeriesStatus.Stopped, hash);
        var r5 = await SeedBatteryRunAsync(f, created.Id, BenchmarkRunSeriesStatus.Completed, new string('b', 64));

        await SeedAnalysisAsync(f, r1, 60, classOne, now.AddHours(-3));
        await SeedAnalysisAsync(f, r2, 70, classOne, now.AddHours(-2));
        await SeedAnalysisAsync(f, r3, 80, classTwo, now.AddHours(-1));
        await SeedAnalysisAsync(f, r4, 50, classOne, now.AddHours(-4));
        await SeedAnalysisAsync(f, r4, null, null, now.AddMinutes(-30));
        await SeedAnalysisAsync(f, r5, 99, classOne, now);

        var dto = Assert.IsAssignableFrom<IEnumerable<BenchmarkBatteryDto>>(
            Assert.IsType<OkObjectResult>(await f.Controller.GetBatteries(ct)).Value).Single();

        // r4's latest analysis is incomplete, and r5 ran an earlier definition.
        Assert.Equal(3, dto.RankedResultCount);
        Assert.Equal((DateTime?)now.AddMinutes(-30), dto.LatestAnalysisAtUtc);

        var board = Assert.IsType<BenchmarkBatteryLeaderboardDto>(
            Assert.IsType<OkObjectResult>(await f.Controller.GetLeaderboard(hash, ct)).Value);
        Assert.Equal(dto.RankedResultCount, board.Classes.Sum(c => c.Rows.Count));
    }

    // --- AdminBenchmarkController additions --------------------------------------------------------

    private static AdminBenchmarkController CreateBenchmarkController(ApplicationDbContext db, BenchmarkRunManager runManager)
        => new(
            db, null!, null!, runManager, null!, null!, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!)
        {
            ControllerContext = UserContext()
        };

    [Fact]
    public async Task InPlaceReruns_AreRefusedWhileAnOrchestratorHoldsTheClaim()
    {
        var f = await CreateFixtureAsync();
        var runManager = new BenchmarkRunManager();
        var controller = CreateBenchmarkController(f.Db, runManager);

        Assert.True(runManager.TryClaimOrchestrator(BenchmarkRunManager.BatteryOwner(5)));
        var battery = Assert.IsType<ConflictObjectResult>(await controller.RerunFailedQuestions(1));
        Assert.Equal("A battery is running; wait for it or cancel it.", battery.Value);

        runManager.ReleaseOrchestrator(BenchmarkRunManager.BatteryOwner(5));
        Assert.True(runManager.TryClaimOrchestrator(BenchmarkRunManager.SeriesOwner(3)));
        var series = Assert.IsType<ConflictObjectResult>(await controller.RetryFailedAssessments(1, null));
        Assert.Equal("A benchmark series is running; wait for it or cancel it.", series.Value);
    }

    [Fact]
    public void StopReasonText_DescribesAnInstrumentChange()
    {
        Assert.Equal(
            "A member is not comparable with the others",
            AdminBenchmarkController.DescribeStopReason(BenchmarkRunSeriesStopReason.InstrumentChanged));
        Assert.Null(AdminBenchmarkController.DescribeStopReason(null));
    }

    [Fact]
    public async Task RunSummaries_CarryTheirBatteryPosition()
    {
        var f = await CreateFixtureAsync();
        var ct = TestContext.Current.CancellationToken;
        var batteryRun = await SeedBatteryRunAsync(f, null, BenchmarkRunSeriesStatus.Completed);
        var member = await SeedMemberAsync(f, batteryRun, suiteIndex: 1);
        var loose = new BenchmarkRun
        {
            BenchmarkSuiteId = f.SuiteA.Id,
            SuiteName = f.SuiteA.Name,
            TestedModelSnapshot = BenchmarkModelSnapshots.Candidate(),
            AssessorModelSnapshot = BenchmarkModelSnapshots.Assessor(),
            Status = BenchmarkRunStatus.Completed
        };
        f.Db.BenchmarkRuns.Add(loose);
        await f.Db.SaveChangesAsync(ct);

        var controller = CreateBenchmarkController(f.Db, new BenchmarkRunManager());
        var summaries = Assert.IsAssignableFrom<IEnumerable<BenchmarkRunSummaryDto>>(
            Assert.IsType<OkObjectResult>(await controller.GetRuns(null, null)).Value).ToList();

        var inBattery = summaries.Single(s => s.Id == member.Id);
        Assert.Equal(batteryRun.Id, inBattery.BatteryRunId);
        Assert.Equal("Pair", inBattery.BatteryName);
        Assert.Equal(2, inBattery.BatterySuitePosition);
        Assert.Equal(2, inBattery.BatterySuiteCount);

        var alone = summaries.Single(s => s.Id == loose.Id);
        Assert.Null(alone.BatteryRunId);
        Assert.Null(alone.BatterySuitePosition);
    }

    [Fact]
    public async Task RunList_ReturnsTheNewest50ByDefault_AndMoreThan200WhenAsked()
    {
        var f = await CreateFixtureAsync();
        var ct = TestContext.Current.CancellationToken;
        var start = DateTime.UtcNow.AddDays(-1);
        f.Db.BenchmarkRuns.AddRange(Enumerable.Range(0, 201).Select(i => new BenchmarkRun
        {
            BenchmarkSuiteId = f.SuiteA.Id,
            SuiteName = f.SuiteA.Name,
            TestedModelSnapshot = BenchmarkModelSnapshots.Candidate(),
            AssessorModelSnapshot = BenchmarkModelSnapshots.Assessor(),
            Status = BenchmarkRunStatus.Completed,
            StartedAtUtc = start.AddMinutes(i)
        }));
        await f.Db.SaveChangesAsync(ct);

        var controller = CreateBenchmarkController(f.Db, new BenchmarkRunManager());

        List<BenchmarkRunSummaryDto> ReadRuns(IActionResult result)
            => Assert.IsAssignableFrom<IEnumerable<BenchmarkRunSummaryDto>>(Assert.IsType<OkObjectResult>(result).Value).ToList();

        Assert.Equal(50, ReadRuns(await controller.GetRuns(null, null)).Count);
        Assert.Equal(201, ReadRuns(await controller.GetRuns(null, 1000)).Count);
        Assert.Equal(201, ReadRuns(await controller.GetRuns(null, 5000)).Count);
    }
}
