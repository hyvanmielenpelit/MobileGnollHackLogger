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
/// The model batch API: start and its refusals, the active and listed batches, the batch DTO and its
/// wire shape, the diagnostics text, skip, delete, the outcome-to-status mapping, and the
/// <c>maxModelsPerBatch</c> the run limits carry.
/// </summary>
public class AdminBenchmarkModelBatchesControllerTests
{
    private const string OwnerId = "admin-1";

    /// <summary>An orchestrator whose guardrails the test supplies and whose members finish at once.</summary>
    private sealed class QuickOrchestrator : BenchmarkModelBatchOrchestrator
    {
        public QuickOrchestrator(IServiceScopeFactory scopeFactory, BenchmarkRunManager runManager)
            : base(
                scopeFactory,
                runManager,
                new BenchmarkSeriesOrchestrator(scopeFactory, runManager, NullLogger<BenchmarkSeriesOrchestrator>.Instance),
                new BenchmarkBatteryOrchestrator(scopeFactory, runManager, NullLogger<BenchmarkBatteryOrchestrator>.Instance),
                NullLogger<BenchmarkModelBatchOrchestrator>.Instance)
        {
            MemberPollInterval = TimeSpan.FromMilliseconds(10);
            CapRetryInterval = TimeSpan.FromMilliseconds(10);
        }

        public List<BenchmarkModelBatchFindingDto> Findings { get; } = new();

        protected override Task<IReadOnlyList<BenchmarkModelBatchFindingDto>> EvaluateGuardrailsAsync(
            IServiceProvider services, StartBenchmarkModelBatchRequest request, string? activeBatchDescription, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<BenchmarkModelBatchFindingDto>>(Findings.ToList());

        protected override async Task<BenchmarkModelBatchLaunch> LaunchChildAsync(
            IServiceProvider services, BenchmarkModelBatchRun batch, BenchmarkModelBatchMember member,
            StartBenchmarkRunRequest request, string batchOwner, CancellationToken ct)
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            var run = BenchmarkModelSnapshots.Attach(new BenchmarkRun
            {
                BenchmarkSuiteId = batch.BenchmarkSuiteId,
                SuiteName = "Suite A",
                TestedModelConfigurationId = member.TestedModelConfigurationId,
                Status = BenchmarkRunStatus.Completed,
                StartedAtUtc = DateTime.UtcNow,
                CompletedAtUtc = DateTime.UtcNow,
                QualityIndex = 70,
                HarnessVersion = BenchmarkAssessmentPrompt.HarnessVersion,
                ScoringMethodVersion = BenchmarkAssessmentPrompt.ScoringMethodVersion
            });
            db.BenchmarkRuns.Add(run);
            await db.SaveChangesAsync(ct);
            return new BenchmarkModelBatchLaunch { Kind = BenchmarkModelBatchLaunchKind.Started, RunId = run.Id };
        }

        protected override Task<BenchmarkInstrumentFingerprint?> ComputeCurrentFingerprintAsync(
            IServiceProvider services, ApplicationDbContext db, long suiteId, long testedModelConfigurationId,
            StartBenchmarkRunRequest template, CancellationToken ct)
            => Task.FromResult<BenchmarkInstrumentFingerprint?>(null);
    }

    private sealed record Fixture(
        AdminBenchmarkModelBatchesController Controller,
        ApplicationDbContext Db,
        QuickOrchestrator Orchestrator,
        BenchmarkRunManager RunManager,
        IConfiguration Configuration,
        long SuiteId,
        string DbName);

    private static ApplicationDbContext CreateDbContext(string name)
        => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options);

    private static async Task<Fixture> CreateFixtureAsync(Dictionary<string, string?>? settings = null)
    {
        string dbName = Guid.NewGuid().ToString();
        var values = new Dictionary<string, string?>
        {
            { "Benchmark:Compliance:MaxRunsPerDay", "500" },
            { "Benchmark:Compliance:MaxRunsPerHour", "100" }
        };
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>()) values[key] = value;
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var services = new ServiceCollection();
        services.AddSingleton(config);
        services.AddScoped(_ => CreateDbContext(dbName));
        services.AddScoped<BenchmarkComplianceGuard>();
        var factory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var db = CreateDbContext(dbName);
        var suite = new BenchmarkSuite { Name = "Suite A", Description = "Desc" };
        db.BenchmarkSuites.Add(suite);
        foreach (var (id, provider, model) in new[] { (1L, "Anthropic", "claude-opus-5"), (2L, "OpenAI", "gpt-5") })
        {
            db.SystemAiApiConfigurations.Add(new SystemAiApiConfiguration
            {
                Id = id, Provider = provider, ModelId = model, DisplayName = model,
                IsEnabled = true, EncryptedApiKey = "encrypted", ModelRole = 4
            });
        }
        await db.SaveChangesAsync();

        var runManager = new BenchmarkRunManager();
        var orchestrator = new QuickOrchestrator(factory, runManager);
        var controller = new AdminBenchmarkModelBatchesController(db, orchestrator, runManager, config)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, OwnerId) }, "TestAuth"))
                }
            }
        };

        return new Fixture(controller, db, orchestrator, runManager, config, suite.Id, dbName);
    }

    private static StartBenchmarkModelBatchRequest Request(Fixture f) => new()
    {
        TargetKind = BenchmarkModelBatchTargetKind.Suite,
        SuiteId = f.SuiteId,
        TestedModelConfigurationIds = new List<long> { 1, 2 },
        RunsPerModel = 1,
        Order = BenchmarkModelBatchOrder.AsListed,
        Run = new StartBenchmarkRunRequest { AssessorModelConfigurationId = 9 }
    };

    /// <summary>A batch row with one member per status given, the first holding a completed run.</summary>
    private static async Task<BenchmarkModelBatchRun> SeedBatchAsync(
        Fixture f,
        BenchmarkRunSeriesStatus status,
        BenchmarkModelBatchStopReason? stopReason = null,
        DateTime? createdAtUtc = null,
        params BenchmarkModelBatchMemberStatus[] members)
    {
        var run = BenchmarkModelSnapshots.Attach(new BenchmarkRun
        {
            BenchmarkSuiteId = f.SuiteId,
            SuiteName = "Suite A",
            TestedModelConfigurationId = 1,
            Status = BenchmarkRunStatus.Completed,
            StartedAtUtc = DateTime.UtcNow.AddHours(-1),
            CompletedAtUtc = DateTime.UtcNow.AddMinutes(-30),
            QualityIndex = 70,
            QualityIndexStandardError = 2,
            AnsweredQuestionCount = 2,
            ClaimsRefutedCount = 1,
            ToolGuidesSha256 = "guides-1",
            HarnessVersion = BenchmarkAssessmentPrompt.HarnessVersion,
            ScoringMethodVersion = BenchmarkAssessmentPrompt.ScoringMethodVersion
        });
        f.Db.BenchmarkRuns.Add(run);
        await f.Db.SaveChangesAsync();

        f.Db.BenchmarkRunAnswers.Add(new BenchmarkRunAnswer
        {
            BenchmarkRunId = run.Id, OrderIndex = 1, QuestionText = "Q1", AnswerText = "A1",
            Status = BenchmarkAnswerStatus.Ok, DurationMs = 4000, ToolTimeMs = 1000
        });
        f.Db.BenchmarkRunAnswers.Add(new BenchmarkRunAnswer
        {
            BenchmarkRunId = run.Id, OrderIndex = 2, QuestionText = "Q2", AnswerText = string.Empty,
            Status = BenchmarkAnswerStatus.ProviderError, DurationMs = 0
        });
        await f.Db.SaveChangesAsync();

        var statuses = members.Length == 0 ? new[] { BenchmarkModelBatchMemberStatus.Completed } : members;
        var batch = new BenchmarkModelBatchRun
        {
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow,
            CreatedByUserId = OwnerId,
            TargetKind = BenchmarkModelBatchTargetKind.Suite,
            BenchmarkSuiteId = f.SuiteId,
            TargetName = "Suite A",
            RunsPerModel = 1,
            Order = BenchmarkModelBatchOrder.Randomized,
            OrderSeed = 42,
            StartRequestJson = JsonSerializer.Serialize(new StartBenchmarkRunRequest { AssessorModelConfigurationId = 2 }),
            Status = status,
            StopReason = stopReason,
            StopDetail = stopReason.HasValue ? "Stopped by the test." : null,
            AcknowledgedFindingsJson = "[{\"code\":\"MB-W03\",\"severity\":\"Warning\",\"title\":\"No claim verifier\",\"detail\":\"d\",\"modelConfigurationIds\":[],\"acknowledgmentKey\":\"MB-W03\"}]",
            FirstMemberToolGuidesSha256 = "guides-1",
            FirstMemberHarnessVersion = BenchmarkAssessmentPrompt.HarnessVersion,
            RequestedMemberCount = statuses.Length,
            LastProgressAtUtc = DateTime.UtcNow,
            Members = statuses.Select((s, i) => new BenchmarkModelBatchMember
            {
                OrderIndex = i,
                TestedModelConfigurationId = i + 1,
                TestedModelSnapshotJson = JsonSerializer.Serialize(
                    new BenchmarkModelBatchModelDto { DisplayName = $"Model {i + 1}", Provider = "Anthropic", ModelId = $"m{i + 1}" },
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                Status = s,
                BenchmarkRunId = i == 0 ? run.Id : null
            }).ToList()
        };
        f.Db.BenchmarkModelBatchRuns.Add(batch);
        await f.Db.SaveChangesAsync();
        return batch;
    }

    private static async Task WaitForStatusAsync(Fixture f, long id, BenchmarkRunSeriesStatus status)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            using (var db = CreateDbContext(f.DbName))
            {
                var current = await db.BenchmarkModelBatchRuns.AsNoTracking().Where(b => b.Id == id).Select(b => b.Status)
                    .SingleAsync(TestContext.Current.CancellationToken);
                if (current == status) break;
                if (DateTime.UtcNow > deadline) throw new TimeoutException($"Batch #{id} stayed {current}.");
            }
            await Task.Delay(15, TestContext.Current.CancellationToken);
        }

        while (f.Orchestrator.IsDriving(id) || f.RunManager.BatchOwner != null)
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The batch kept driving.");
            await Task.Delay(15, TestContext.Current.CancellationToken);
        }
    }

    // --- Start ------------------------------------------------------------------------------------

    [Fact]
    public async Task StartBatch_Returns201WithTheBatch()
    {
        var f = await CreateFixtureAsync();

        var created = Assert.IsType<ObjectResult>(await f.Controller.StartBatch(Request(f), TestContext.Current.CancellationToken));
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        var dto = Assert.IsType<BenchmarkModelBatchRunDto>(created.Value);
        Assert.Equal("Suite", dto.TargetKind);
        Assert.Equal(2, dto.Members.Count);
        Assert.Equal("claude-opus-5", dto.Members[0].Model.DisplayName);

        await WaitForStatusAsync(f, dto.Id, BenchmarkRunSeriesStatus.Completed);
    }

    [Fact]
    public async Task StartBatch_Refuses400WithTheBlockers_And409WithTheWarningsToAcknowledge()
    {
        var f = await CreateFixtureAsync();
        f.Orchestrator.Findings.Add(new BenchmarkModelBatchFindingDto { Code = "MB-B01", Severity = BenchmarkModelBatchSeverity.Blocker, Detail = "Too few." });

        var blocked = Assert.IsType<BadRequestObjectResult>(await f.Controller.StartBatch(Request(f), TestContext.Current.CancellationToken));
        Assert.Equal("MB-B01", Assert.Single(Assert.IsType<BenchmarkModelBatchRefusalDto>(blocked.Value).Findings).Code);

        f.Orchestrator.Findings.Clear();
        f.Orchestrator.Findings.Add(new BenchmarkModelBatchFindingDto
        {
            Code = "MB-W03", Severity = BenchmarkModelBatchSeverity.Warning, AcknowledgmentKey = "MB-W03"
        });

        var conflict = Assert.IsType<ConflictObjectResult>(await f.Controller.StartBatch(Request(f), TestContext.Current.CancellationToken));
        Assert.Equal("MB-W03", Assert.Single(Assert.IsType<BenchmarkModelBatchRefusalDto>(conflict.Value).Findings).AcknowledgmentKey);
        Assert.Empty(await f.Db.BenchmarkModelBatchRuns.ToListAsync(TestContext.Current.CancellationToken));
    }

    // --- Reading ----------------------------------------------------------------------------------

    [Fact]
    public async Task GetActiveBatch_Is204WithNothingToShow_AndTheStoppedBatchOtherwise()
    {
        var f = await CreateFixtureAsync();
        await SeedBatchAsync(f, BenchmarkRunSeriesStatus.Completed);

        Assert.IsType<NoContentResult>(await f.Controller.GetActiveBatch(TestContext.Current.CancellationToken));

        var stopped = await SeedBatchAsync(f, BenchmarkRunSeriesStatus.Stopped, BenchmarkModelBatchStopReason.MemberStopped, null,
            BenchmarkModelBatchMemberStatus.Completed, BenchmarkModelBatchMemberStatus.Failed, BenchmarkModelBatchMemberStatus.Pending);

        var dto = Assert.IsType<BenchmarkModelBatchRunDto>(
            Assert.IsType<OkObjectResult>(await f.Controller.GetActiveBatch(TestContext.Current.CancellationToken)).Value);
        Assert.Equal(stopped.Id, dto.Id);
        Assert.Equal("Stopped", dto.Status);
        Assert.Equal("MemberStopped", dto.StopReason);
        Assert.True(dto.Resumable);
        Assert.Equal(new[] { BenchmarkModelBatchResumeMode.Continue, BenchmarkModelBatchResumeMode.SkipCurrent },
            dto.ResumeOptions.Select(o => o.Mode).ToArray());
    }

    [Fact]
    public async Task GetBatch_CarriesEachMembersResult_Instrument_AndTheBatchsSettings()
    {
        var f = await CreateFixtureAsync();
        var batch = await SeedBatchAsync(f, BenchmarkRunSeriesStatus.Completed);

        var dto = Assert.IsType<BenchmarkModelBatchRunDto>(
            Assert.IsType<OkObjectResult>(await f.Controller.GetBatch(batch.Id, TestContext.Current.CancellationToken)).Value);

        Assert.Equal("Completed", dto.Status);
        Assert.Equal("Randomized", dto.Order);
        Assert.Equal(42, dto.OrderSeed);
        Assert.Equal(2, dto.Run!.AssessorModelConfigurationId);
        Assert.False(dto.Resumable);
        Assert.Empty(dto.ResumeOptions);
        Assert.Equal("MB-W03", Assert.Single(dto.AcknowledgedFindings).Code);
        Assert.Equal("guides-1", dto.FirstMemberInstrument!.ToolGuidesSha256);
        Assert.Equal(15, dto.StallMinutes);

        var member = Assert.Single(dto.Members);
        Assert.Equal("Model 1", member.Model.DisplayName);
        Assert.Equal(1, member.Model.ConfigurationId);
        Assert.Equal("Completed", member.Status);
        Assert.Single(member.RunIds);
        Assert.Empty(member.InstrumentDriftKeys);

        var result = member.Result!;
        Assert.Equal(70d, result.IntelligenceIndex!.Value);
        Assert.Equal(3.92, result.IndexHalfWidth!.Value, 6);
        Assert.Equal("run", result.IndexSource);
        Assert.Equal(3000d, result.MedianModelTimeMs!.Value);
        Assert.Equal(1, result.RefutedClaims);
        Assert.Equal(1, result.FailedAnswers);
        Assert.Equal(1, result.ProviderErrors);

        Assert.IsType<NotFoundResult>(await f.Controller.GetBatch(4242, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetBatches_ListsNewestFirst_WithoutResults_AndPages()
    {
        var f = await CreateFixtureAsync();
        var older = await SeedBatchAsync(f, BenchmarkRunSeriesStatus.Completed, createdAtUtc: DateTime.UtcNow.AddDays(-1));
        var newer = await SeedBatchAsync(f, BenchmarkRunSeriesStatus.Cancelled, createdAtUtc: DateTime.UtcNow);

        var list = Assert.IsType<List<BenchmarkModelBatchRunDto>>(
            Assert.IsType<OkObjectResult>(await f.Controller.GetBatches(null, null, TestContext.Current.CancellationToken)).Value);
        Assert.Equal(new[] { newer.Id, older.Id }, list.Select(b => b.Id).ToArray());
        Assert.All(list.SelectMany(b => b.Members), m => Assert.Null(m.Result));

        var second = Assert.IsType<List<BenchmarkModelBatchRunDto>>(
            Assert.IsType<OkObjectResult>(await f.Controller.GetBatches(1, 1, TestContext.Current.CancellationToken)).Value);
        Assert.Equal(older.Id, Assert.Single(second).Id);
    }

    [Fact]
    public async Task TheBatchDto_SerializesItsEnumsAsStrings()
    {
        var f = await CreateFixtureAsync();
        var batch = await SeedBatchAsync(f, BenchmarkRunSeriesStatus.Stopped, BenchmarkModelBatchStopReason.InstrumentChanged, null,
            BenchmarkModelBatchMemberStatus.Completed, BenchmarkModelBatchMemberStatus.Pending);

        var dto = Assert.IsType<BenchmarkModelBatchRunDto>(
            Assert.IsType<OkObjectResult>(await f.Controller.GetBatch(batch.Id, TestContext.Current.CancellationToken)).Value);
        string json = JsonSerializer.Serialize(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"status\":\"Stopped\"", json);
        Assert.Contains("\"stopReason\":\"InstrumentChanged\"", json);
        Assert.Contains("\"targetKind\":\"Suite\"", json);
        Assert.Contains("\"mode\":\"RerunUnderCurrentInstrument\"", json);
        Assert.Contains("\"mode\":\"AcceptInstrumentChange\"", json);
        Assert.Contains("\"severity\":\"Warning\"", json);
        Assert.Contains("\"status\":\"Pending\"", json);

        var request = JsonSerializer.Deserialize<StartBenchmarkModelBatchRequest>(
            "{\"targetKind\":\"Battery\",\"batteryId\":3,\"order\":\"AsListed\",\"testedModelConfigurationIds\":[1,2]}",
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(BenchmarkModelBatchTargetKind.Battery, request.TargetKind);
        Assert.Equal(BenchmarkModelBatchOrder.AsListed, request.Order);

        var resume = JsonSerializer.Deserialize<ResumeBenchmarkModelBatchRequest>(
            "{\"mode\":\"SkipCurrent\"}", new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(BenchmarkModelBatchResumeMode.SkipCurrent, resume.Mode);
    }

    [Fact]
    public async Task GetDiagnostics_IsPlainText_WithEverySection()
    {
        var f = await CreateFixtureAsync();
        var batch = await SeedBatchAsync(f, BenchmarkRunSeriesStatus.Completed);

        var content = Assert.IsType<ContentResult>(await f.Controller.GetDiagnostics(batch.Id, TestContext.Current.CancellationToken));

        Assert.StartsWith("text/plain", content.ContentType);
        foreach (string section in new[] { "BATCH", "SETTINGS", "GUARDRAILS", "ORDER", "MEMBERS", "INSTRUMENT", "TIMING", "ERRORS", "CAPS", "PROGRESS" })
        {
            Assert.Contains("\n" + section + Environment.NewLine, "\n" + content.Content);
        }
        Assert.Contains("Seed: 42", content.Content);
        Assert.Contains("Acknowledged MB-W03", content.Content);
        Assert.Contains("gpt-5 (#2)", content.Content);
    }

    // --- Skip, delete, mapping --------------------------------------------------------------------

    [Fact]
    public async Task SkipMember_SkipsAPendingMember_AndRefusesAnyOther()
    {
        var f = await CreateFixtureAsync();
        var batch = await SeedBatchAsync(f, BenchmarkRunSeriesStatus.Stopped, BenchmarkModelBatchStopReason.RunCapReached, null,
            BenchmarkModelBatchMemberStatus.Completed, BenchmarkModelBatchMemberStatus.Pending);
        var pending = batch.Members.Single(m => m.OrderIndex == 1);
        var done = batch.Members.Single(m => m.OrderIndex == 0);

        var dto = Assert.IsType<BenchmarkModelBatchRunDto>(Assert.IsType<OkObjectResult>(
            await f.Controller.SkipMember(batch.Id, pending.Id, TestContext.Current.CancellationToken)).Value);
        Assert.Equal("Skipped", dto.Members.Single(m => m.Id == pending.Id).Status);

        Assert.IsType<BadRequestObjectResult>(await f.Controller.SkipMember(batch.Id, done.Id, TestContext.Current.CancellationToken));
        Assert.IsType<NotFoundObjectResult>(await f.Controller.SkipMember(batch.Id, 4242, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteBatch_RefusesALiveOrStoppedBatch_AndKeepsTheRunsOfAFinishedOne()
    {
        var f = await CreateFixtureAsync();
        var stopped = await SeedBatchAsync(f, BenchmarkRunSeriesStatus.Stopped, BenchmarkModelBatchStopReason.MemberStopped);
        var finished = await SeedBatchAsync(f, BenchmarkRunSeriesStatus.Completed);
        long runId = finished.Members.Single().BenchmarkRunId!.Value;

        Assert.IsType<ConflictObjectResult>(await f.Controller.DeleteBatch(stopped.Id, TestContext.Current.CancellationToken));
        Assert.IsType<NoContentResult>(await f.Controller.DeleteBatch(finished.Id, TestContext.Current.CancellationToken));
        Assert.IsType<NotFoundResult>(await f.Controller.DeleteBatch(finished.Id, TestContext.Current.CancellationToken));

        Assert.True(await f.Db.BenchmarkRuns.AnyAsync(r => r.Id == runId, TestContext.Current.CancellationToken));
        Assert.Empty(await f.Db.BenchmarkModelBatchMembers.Where(m => m.BenchmarkModelBatchRunId == finished.Id)
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Outcomes_MapToHttp()
    {
        Assert.IsType<NotFoundObjectResult>(AdminBenchmarkModelBatchesController.ResultToActionResult(
            BenchmarkModelBatchResult.Fail(BenchmarkModelBatchOutcome.NotFound, "Model batch not found.")));
        Assert.IsType<BadRequestObjectResult>(AdminBenchmarkModelBatchesController.ResultToActionResult(
            BenchmarkModelBatchResult.Fail(BenchmarkModelBatchOutcome.Invalid, "No.")));
        Assert.Equal("Busy.", Assert.IsType<ConflictObjectResult>(AdminBenchmarkModelBatchesController.ResultToActionResult(
            BenchmarkModelBatchResult.Fail(BenchmarkModelBatchOutcome.Conflict, "Busy."))).Value);

        var changed = Assert.IsType<ConflictObjectResult>(AdminBenchmarkModelBatchesController.ResultToActionResult(
            new BenchmarkModelBatchResult
            {
                Outcome = BenchmarkModelBatchOutcome.InstrumentChanged,
                BatchId = 5,
                Error = "Moved.",
                ChangedKeys = new[] { "ToolGuidesSha256" }
            }));
        string json = JsonSerializer.Serialize(changed.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("\"instrumentChanged\":true", json);
        Assert.Contains("\"changedKeys\":[\"ToolGuidesSha256\"]", json);
    }

    [Fact]
    public async Task RunLimits_CarryMaxModelsPerBatch_FromConfiguration()
    {
        var f = await CreateFixtureAsync(new Dictionary<string, string?> { { "Benchmark:ModelBatch:MaxModels", "7" } });
        var guard = new BenchmarkComplianceGuard(f.Configuration, f.Db);
        var controller = new AdminBenchmarkController(
            f.Db, null!, null!, f.RunManager, null!, guard, null!, null!, null!, null!,
            null!, null!, null!, null!, null!, null!, null!, null!, null!);

        var limits = Assert.IsType<BenchmarkRunLimitsDto>(Assert.IsType<OkObjectResult>(await controller.GetRunLimits(f.Configuration)).Value);
        Assert.Equal(7, limits.MaxModelsPerBatch);

        var defaults = Assert.IsType<BenchmarkRunLimitsDto>(Assert.IsType<OkObjectResult>(await controller.GetRunLimits()).Value);
        Assert.Equal(12, defaults.MaxModelsPerBatch);
    }
}
