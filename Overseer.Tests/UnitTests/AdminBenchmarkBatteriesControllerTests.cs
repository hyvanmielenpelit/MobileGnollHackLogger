namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
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
using Overseer.Services.Benchmarking;
using Overseer.Tests.Helpers;
using Xunit;

/// <summary>
/// The battery API: definitions (create, edit, revision and hash), the start refusal of a broken
/// battery, the outcome-to-status mapping, attaching existing runs (reuse preview, attach,
/// candidates), delete while a battery run is active, the battery-run grid, the leaderboard, and the <see cref="AdminBenchmarkController"/> additions (re-run refusal
/// under an orchestrator claim, the stop-reason text, the run-summary battery fields).
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
            new BenchmarkBatteryAnalysisService(db, NullLogger<BenchmarkBatteryAnalysisService>.Instance))
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
}
