namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Concurrent;
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
using Overseer.Services.Benchmarking;
using Overseer.Tests.Helpers;
using Xunit;

/// <summary>
/// Model batch execution: start and its guardrail refusals, the sequential launch order and the
/// stored shuffle seed, a member that fails, skip, cancel mid-member (with a child cancel that
/// throws), the instrument and grader guards with both resume paths, startup reconciliation and the
/// run-cap wait. The children are runs the test writes and finishes itself; no model is called.
/// </summary>
public class BenchmarkModelBatchOrchestratorTests
{
    // --- Fixture ------------------------------------------------------------------------------

    /// <summary>The orchestrator with its children, guardrails and instrument supplied by the test.</summary>
    private sealed class TestOrchestrator : BenchmarkModelBatchOrchestrator
    {
        public TestOrchestrator(IServiceScopeFactory scopeFactory, BenchmarkRunManager runManager)
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

        /// <summary>The tested configuration of every launch, in launch order.</summary>
        public ConcurrentQueue<long> Launched { get; } = new();

        /// <summary>The request each configuration was last launched with.</summary>
        public ConcurrentDictionary<long, StartBenchmarkRunRequest> Requests { get; } = new();

        /// <summary>The status a launched run is written with; Running leaves it for the test to finish.</summary>
        public Func<long, BenchmarkRunStatus> Outcome { get; set; } = _ => BenchmarkRunStatus.Completed;

        /// <summary>The assessor snapshot a configuration's run records; the default assessor when null.</summary>
        public Func<long, SystemAiConfigurationSnapshot?> AssessorOf { get; set; } = _ => null;

        public BenchmarkInstrumentFingerprint Fingerprint { get; set; } = new("prompt-1", "guides-1", "kb-1", "wiki-1", "source-1");

        public HashSet<long> RefuseLaunchOf { get; } = new();

        public bool ThrowOnCancelChild { get; set; }

        public ConcurrentQueue<long> CanceledMembers { get; } = new();

        /// <summary>What each launch was told it replaces, in launch order.</summary>
        public ConcurrentQueue<(long ConfigId, BenchmarkModelBatchChildReplacement? Replaces)> ReplacesOf { get; } = new();

        public int Seed { get; set; } = 12345;

        protected override int NewSeed() => Seed;

        protected override Task<IReadOnlyList<BenchmarkModelBatchFindingDto>> EvaluateGuardrailsAsync(
            IServiceProvider services, StartBenchmarkModelBatchRequest request, string? activeBatchDescription, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<BenchmarkModelBatchFindingDto>>(Findings.ToList());

        protected override async Task<BenchmarkModelBatchLaunch> LaunchChildAsync(
            IServiceProvider services,
            BenchmarkModelBatchRun batch,
            BenchmarkModelBatchMember member,
            StartBenchmarkRunRequest request,
            string batchOwner,
            BenchmarkModelBatchChildReplacement? replaces,
            CancellationToken ct)
        {
            long configId = member.TestedModelConfigurationId;
            Launched.Enqueue(configId);
            Requests[configId] = request;
            ReplacesOf.Enqueue((configId, replaces));

            if (RefuseLaunchOf.Contains(configId))
            {
                return BenchmarkModelBatchLaunch.Refused(BenchmarkModelBatchLaunchKind.Refused, "Refused by the test.");
            }

            var db = services.GetRequiredService<ApplicationDbContext>();
            var status = Outcome(configId);
            var run = BenchmarkModelSnapshots.Attach(new BenchmarkRun
            {
                BenchmarkSuiteId = batch.BenchmarkSuiteId,
                BenchmarkSuiteIdUsed = batch.BenchmarkSuiteId,
                SuiteName = "Suite",
                TestedModelConfigurationId = configId,
                AssessorModelSnapshot = AssessorOf(configId)!,
                Status = status,
                StartedAtUtc = DateTime.UtcNow,
                CompletedAtUtc = status == BenchmarkRunStatus.Running ? null : DateTime.UtcNow,
                QualityIndex = 70,
                CandidateSystemPromptSha256 = Fingerprint.CandidateSystemPromptSha256,
                ToolGuidesSha256 = Fingerprint.ToolGuidesSha256,
                KnowledgeBaseHeadSha = Fingerprint.KnowledgeBaseHeadSha,
                WikiHeadSha = Fingerprint.WikiHeadSha,
                SourceCodeHeadSha = Fingerprint.SourceCodeHeadSha,
                HarnessVersion = BenchmarkAssessmentPrompt.HarnessVersion,
                ScoringMethodVersion = BenchmarkAssessmentPrompt.ScoringMethodVersion,
                ErrorMessage = status == BenchmarkRunStatus.Failed ? "The provider failed." : null,
                ReplacesRunId = replaces?.Run?.RunId,
                ReplacementCause = replaces?.Run?.Cause
            });
            db.BenchmarkRuns.Add(run);
            await db.SaveChangesAsync(ct);

            return new BenchmarkModelBatchLaunch { Kind = BenchmarkModelBatchLaunchKind.Started, RunId = run.Id };
        }

        protected override Task CancelChildAsync(IServiceProvider services, BenchmarkModelBatchMember member, CancellationToken ct)
        {
            CanceledMembers.Enqueue(member.Id);
            if (ThrowOnCancelChild) throw new InvalidOperationException("The child cancel failed.");
            return Task.CompletedTask;
        }

        protected override Task<BenchmarkInstrumentFingerprint?> ComputeCurrentFingerprintAsync(
            IServiceProvider services, ApplicationDbContext db, long suiteId, long testedModelConfigurationId,
            StartBenchmarkRunRequest template, CancellationToken ct)
            => Task.FromResult<BenchmarkInstrumentFingerprint?>(Fingerprint);
    }

    private sealed record Fixture(string DbName, BenchmarkRunManager RunManager, TestOrchestrator Orchestrator, long SuiteId);

    private static ApplicationDbContext CreateDbContext(string name)
        => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options);

    private static async Task<Fixture> CreateFixtureAsync(int maxRunsPerHour = 100)
    {
        string dbName = Guid.NewGuid().ToString();
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Benchmark:Compliance:MaxRunsPerDay", "500" },
                { "Benchmark:Compliance:MaxRunsPerHour", maxRunsPerHour.ToString() }
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton(config);
        services.AddScoped(_ => CreateDbContext(dbName));
        services.AddScoped<BenchmarkComplianceGuard>();
        var factory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        using var db = CreateDbContext(dbName);
        var suite = new BenchmarkSuite { Name = "Suite A", Description = "Desc" };
        db.BenchmarkSuites.Add(suite);
        foreach (var (id, provider, model) in new[] { (1L, "Anthropic", "claude-opus-5"), (2L, "OpenAI", "gpt-5"), (3L, "Google", "gemini-pro") })
        {
            db.SystemAiApiConfigurations.Add(new SystemAiApiConfiguration
            {
                Id = id, Provider = provider, ModelId = model, DisplayName = model,
                IsEnabled = true, EncryptedApiKey = "encrypted", ModelRole = 4
            });
        }
        await db.SaveChangesAsync();

        var runManager = new BenchmarkRunManager();
        return new Fixture(dbName, runManager, new TestOrchestrator(factory, runManager), suite.Id);
    }

    private static StartBenchmarkModelBatchRequest Request(
        Fixture fixture,
        BenchmarkModelBatchOrder order = BenchmarkModelBatchOrder.AsListed,
        bool allowCapWait = false,
        params long[] ids)
        => new()
        {
            TargetKind = BenchmarkModelBatchTargetKind.Suite,
            SuiteId = fixture.SuiteId,
            TestedModelConfigurationIds = (ids.Length == 0 ? new long[] { 1, 2, 3 } : ids).ToList(),
            RunsPerModel = 1,
            Order = order,
            AllowCapWait = allowCapWait,
            Run = new StartBenchmarkRunRequest { AssessorModelConfigurationId = 9, ClaimVerifierModelConfigurationId = 8 }
        };

    private static async Task<BenchmarkModelBatchRun> WaitForAsync(
        Fixture fixture, long batchId, Func<BenchmarkModelBatchRun, bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            using (var db = CreateDbContext(fixture.DbName))
            {
                var batch = await db.BenchmarkModelBatchRuns.AsNoTracking().Include(b => b.Members)
                    .SingleAsync(b => b.Id == batchId, TestContext.Current.CancellationToken);
                if (condition(batch)) return batch;
                if (DateTime.UtcNow > deadline) throw new TimeoutException($"Timed out waiting for {what}; status {batch.Status}.");
            }

            await Task.Delay(15, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Waits until the drive task has ended and released the batch claim.</summary>
    private static async Task WaitUntilIdleAsync(Fixture fixture, long batchId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (fixture.Orchestrator.IsDriving(batchId) || fixture.RunManager.BatchOwner != null)
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The batch kept driving.");
            await Task.Delay(15, TestContext.Current.CancellationToken);
        }
    }

    private static async Task<BenchmarkRun> WaitForRunOfAsync(Fixture fixture, long configId, int nth = 1)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            using (var db = CreateDbContext(fixture.DbName))
            {
                var runs = await db.BenchmarkRuns.AsNoTracking()
                    .Where(r => r.TestedModelConfigurationId == configId)
                    .OrderBy(r => r.Id)
                    .ToListAsync(TestContext.Current.CancellationToken);
                if (runs.Count >= nth) return runs[nth - 1];
            }

            if (DateTime.UtcNow > deadline) throw new TimeoutException($"No run {nth} of configuration {configId}.");
            await Task.Delay(15, TestContext.Current.CancellationToken);
        }
    }

    private static async Task FinishRunAsync(Fixture fixture, long runId, BenchmarkRunStatus status)
    {
        using var db = CreateDbContext(fixture.DbName);
        var run = await db.BenchmarkRuns.SingleAsync(r => r.Id == runId, TestContext.Current.CancellationToken);
        run.Status = status;
        run.CompletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static BenchmarkModelBatchFindingDto Finding(string code, string severity, params long[] ids)
        => new()
        {
            Code = code,
            Severity = severity,
            Title = code,
            Detail = code,
            ModelConfigurationIds = ids.ToList(),
            AcknowledgmentKey = severity == BenchmarkModelBatchSeverity.Warning
                ? BenchmarkModelBatchGuardrails.AcknowledgmentKey(code, ids)
                : null
        };

    // --- Start ------------------------------------------------------------------------------------

    [Fact]
    public async Task Start_LaunchesTheMembersOneAfterAnother_InTheListedOrder_AndCompletes()
    {
        var f = await CreateFixtureAsync();

        var result = await f.Orchestrator.StartAsync(Request(f), "admin", TestContext.Current.CancellationToken);
        Assert.Equal(BenchmarkModelBatchOutcome.Ok, result.Outcome);

        var batch = await WaitForAsync(f, result.BatchId!.Value, b => b.Status == BenchmarkRunSeriesStatus.Completed, "completion");
        await WaitUntilIdleAsync(f, batch.Id);

        Assert.Equal(new long[] { 1, 2, 3 }, f.Orchestrator.Launched.ToArray());
        Assert.All(batch.Members, m => Assert.Equal(BenchmarkModelBatchMemberStatus.Completed, m.Status));
        Assert.All(batch.Members, m => Assert.NotNull(m.BenchmarkRunId));
        Assert.Equal(3, batch.CompletedMemberCount);
        Assert.Equal("guides-1", batch.FirstMemberToolGuidesSha256);
        Assert.Equal(BenchmarkAssessmentPrompt.HarnessVersion, batch.FirstMemberHarnessVersion);
        Assert.Null(batch.OrderSeed);
        Assert.Null(f.RunManager.BatchOwner);
    }

    [Fact]
    public async Task Start_Randomized_StoresTheSeed_AndRunsTheSeedsShuffle()
    {
        var f = await CreateFixtureAsync();
        f.Orchestrator.Seed = 777;

        var result = await f.Orchestrator.StartAsync(Request(f, BenchmarkModelBatchOrder.Randomized), "admin", TestContext.Current.CancellationToken);
        var batch = await WaitForAsync(f, result.BatchId!.Value, b => b.Status == BenchmarkRunSeriesStatus.Completed, "completion");

        var expected = BenchmarkModelBatchOrchestrator.Shuffle(new long[] { 1, 2, 3 }, 777);
        Assert.Equal(777, batch.OrderSeed);
        Assert.Equal(expected, batch.Members.OrderBy(m => m.OrderIndex).Select(m => m.TestedModelConfigurationId).ToList());
        Assert.Equal(expected, f.Orchestrator.Launched.ToList());
        Assert.Equal(expected, BenchmarkModelBatchOrchestrator.Shuffle(new long[] { 1, 2, 3 }, 777));
    }

    [Fact]
    public async Task Start_IsRefused_ByABlocker_AndWritesNothing()
    {
        var f = await CreateFixtureAsync();
        f.Orchestrator.Findings.Add(Finding("MB-B01", BenchmarkModelBatchSeverity.Blocker));

        var result = await f.Orchestrator.StartAsync(Request(f), "admin", TestContext.Current.CancellationToken);

        Assert.Equal(BenchmarkModelBatchOutcome.Blocked, result.Outcome);
        Assert.Equal("MB-B01", Assert.Single(result.Findings).Code);
        using var db = CreateDbContext(f.DbName);
        Assert.Empty(await db.BenchmarkModelBatchRuns.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Null(f.RunManager.BatchOwner);
    }

    [Fact]
    public async Task Start_IsAConflict_WhileSomethingRuns()
    {
        var f = await CreateFixtureAsync();
        f.Orchestrator.Findings.Add(Finding("MB-B09", BenchmarkModelBatchSeverity.Blocker));
        Assert.Equal(BenchmarkModelBatchOutcome.Conflict,
            (await f.Orchestrator.StartAsync(Request(f), "admin", TestContext.Current.CancellationToken)).Outcome);

        var g = await CreateFixtureAsync();
        Assert.True(g.RunManager.TryClaimOrchestrator(BenchmarkRunManager.SeriesOwner(4)));
        var raced = await g.Orchestrator.StartAsync(Request(g), "admin", TestContext.Current.CancellationToken);
        Assert.Equal(BenchmarkModelBatchOutcome.Conflict, raced.Outcome);
        using var db = CreateDbContext(g.DbName);
        Assert.Empty(await db.BenchmarkModelBatchRuns.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Start_NeedsEveryWarningAcknowledged_AndCarriesTheSameProviderAcknowledgmentToItsMembers()
    {
        var f = await CreateFixtureAsync();
        var w10 = Finding("MB-W10", BenchmarkModelBatchSeverity.Warning, 2);
        f.Orchestrator.Findings.Add(w10);
        f.Orchestrator.Findings.Add(Finding("MB-A01", BenchmarkModelBatchSeverity.Advice));

        var refused = await f.Orchestrator.StartAsync(Request(f), "admin", TestContext.Current.CancellationToken);
        Assert.Equal(BenchmarkModelBatchOutcome.NeedsAcknowledgment, refused.Outcome);
        Assert.Equal("MB-W10", Assert.Single(refused.Findings).Code);

        var request = Request(f);
        request.AcknowledgedFindingKeys.Add(w10.AcknowledgmentKey!);
        var started = await f.Orchestrator.StartAsync(request, "admin", TestContext.Current.CancellationToken);
        Assert.Equal(BenchmarkModelBatchOutcome.Ok, started.Outcome);

        var batch = await WaitForAsync(f, started.BatchId!.Value, b => b.Status == BenchmarkRunSeriesStatus.Completed, "completion");
        Assert.Equal("MB-W10", Assert.Single(BenchmarkModelBatchOrchestrator.ReadFindings(batch.AcknowledgedFindingsJson)).Code);
        Assert.Equal("MB-A01", Assert.Single(BenchmarkModelBatchOrchestrator.ReadFindings(batch.AdviceAtStartJson)).Code);
        Assert.False(f.Orchestrator.Requests[1].AcknowledgeSameProvider);
        Assert.True(f.Orchestrator.Requests[2].AcknowledgeSameProvider);
        Assert.Equal(2, f.Orchestrator.Requests[2].TestedModelConfigurationId);
        Assert.Equal(f.SuiteId, f.Orchestrator.Requests[2].SuiteId);
    }

    // --- A member that fails, skip and continue ---------------------------------------------------

    [Fact]
    public async Task AFailedMember_StopsTheBatch_AndReleasesTheClaim()
    {
        var f = await CreateFixtureAsync();
        f.Orchestrator.Outcome = id => id == 2 ? BenchmarkRunStatus.Failed : BenchmarkRunStatus.Completed;

        var result = await f.Orchestrator.StartAsync(Request(f), "admin", TestContext.Current.CancellationToken);
        var batch = await WaitForAsync(f, result.BatchId!.Value, b => b.Status == BenchmarkRunSeriesStatus.Stopped, "the stop");
        await WaitUntilIdleAsync(f, batch.Id);

        Assert.Equal(BenchmarkModelBatchStopReason.MemberStopped, batch.StopReason);
        Assert.Contains("gpt-5 (2 of 3) ended Failed", batch.StopDetail);
        var members = batch.Members.OrderBy(m => m.OrderIndex).ToList();
        Assert.Equal(BenchmarkModelBatchMemberStatus.Completed, members[0].Status);
        Assert.Equal(BenchmarkModelBatchMemberStatus.Failed, members[1].Status);
        Assert.Equal(BenchmarkModelBatchMemberStatus.Pending, members[2].Status);
        Assert.Equal(1, batch.FailedMemberCount);
        Assert.Equal(new long[] { 1, 2 }, f.Orchestrator.Launched.ToArray());
        Assert.Null(f.RunManager.BatchOwner);

        var modes = BenchmarkModelBatchOrchestrator.ResumeOptionsFor(batch, isDriving: false).Select(o => o.Mode).ToList();
        Assert.Equal(new[] { BenchmarkModelBatchResumeMode.Continue, BenchmarkModelBatchResumeMode.SkipCurrent }, modes);
    }

    [Fact]
    public async Task SkipCurrent_MarksTheStoppedMemberSkipped_AndGoesOn()
    {
        var f = await CreateFixtureAsync();
        f.Orchestrator.Outcome = id => id == 2 ? BenchmarkRunStatus.Failed : BenchmarkRunStatus.Completed;
        var result = await f.Orchestrator.StartAsync(Request(f), "admin", TestContext.Current.CancellationToken);
        long id = result.BatchId!.Value;
        await WaitForAsync(f, id, b => b.Status == BenchmarkRunSeriesStatus.Stopped, "the stop");
        await WaitUntilIdleAsync(f, id);

        var resumed = await f.Orchestrator.ResumeAsync(id, BenchmarkModelBatchResumeMode.SkipCurrent, TestContext.Current.CancellationToken);
        Assert.Equal(BenchmarkModelBatchOutcome.Ok, resumed.Outcome);

        var batch = await WaitForAsync(f, id, b => b.Status == BenchmarkRunSeriesStatus.CompletedWithErrors, "completion");
        var members = batch.Members.OrderBy(m => m.OrderIndex).ToList();
        Assert.Equal(BenchmarkModelBatchMemberStatus.Skipped, members[1].Status);
        Assert.Equal(BenchmarkModelBatchMemberStatus.Completed, members[2].Status);
        Assert.Equal(1, batch.SkippedMemberCount);
        Assert.Equal(new long[] { 1, 2, 3 }, f.Orchestrator.Launched.ToArray());
    }

    [Fact]
    public async Task Continue_AdoptsASingleRunMemberWhoseRunHasSinceCompleted()
    {
        var f = await CreateFixtureAsync();
        f.Orchestrator.Outcome = id => id == 2 ? BenchmarkRunStatus.Failed : BenchmarkRunStatus.Completed;
        var result = await f.Orchestrator.StartAsync(Request(f), "admin", TestContext.Current.CancellationToken);
        long id = result.BatchId!.Value;
        var stopped = await WaitForAsync(f, id, b => b.Status == BenchmarkRunSeriesStatus.Stopped, "the stop");
        long runId = stopped.Members.Single(m => m.OrderIndex == 1).BenchmarkRunId!.Value;
        await WaitUntilIdleAsync(f, id);

        await FinishRunAsync(f, runId, BenchmarkRunStatus.Completed);

        Assert.Equal(BenchmarkModelBatchOutcome.Ok,
            (await f.Orchestrator.ResumeAsync(id, BenchmarkModelBatchResumeMode.Continue, TestContext.Current.CancellationToken)).Outcome);

        var batch = await WaitForAsync(f, id, b => b.Status == BenchmarkRunSeriesStatus.Completed, "completion");
        Assert.Equal(new long[] { 1, 2, 3 }, f.Orchestrator.Launched.ToArray());
        var member = batch.Members.Single(m => m.OrderIndex == 1);
        Assert.Equal(BenchmarkModelBatchMemberStatus.Completed, member.Status);
        Assert.Equal(runId, member.BenchmarkRunId);
        Assert.Null(member.ErrorMessage);
        using (var db = CreateDbContext(f.DbName))
        {
            var run = await db.BenchmarkRuns.AsNoTracking().SingleAsync(r => r.Id == runId, TestContext.Current.CancellationToken);
            Assert.Equal(run.CompletedAtUtc, member.CompletedAtUtc);
        }

        Assert.Null(batch.SupersededMembersJson);
        Assert.Equal(3, batch.CompletedMemberCount);
        Assert.Equal(0, batch.FailedMemberCount);
    }

    [Theory]
    [InlineData(BenchmarkRunSeriesStatus.Completed, BenchmarkModelBatchMemberStatus.Completed, BenchmarkRunSeriesStatus.Completed)]
    [InlineData(BenchmarkRunSeriesStatus.CompletedWithErrors, BenchmarkModelBatchMemberStatus.CompletedWithErrors, BenchmarkRunSeriesStatus.CompletedWithErrors)]
    public async Task Continue_AdoptsABatteryMemberWhoseBatteryRunHasSinceFinished(
        BenchmarkRunSeriesStatus batteryStatus,
        BenchmarkModelBatchMemberStatus expectedMember,
        BenchmarkRunSeriesStatus expectedBatch)
    {
        var f = await CreateFixtureAsync();
        f.Orchestrator.Outcome = id => id == 2 ? BenchmarkRunStatus.Failed : BenchmarkRunStatus.Completed;
        var result = await f.Orchestrator.StartAsync(Request(f), "admin", TestContext.Current.CancellationToken);
        long id = result.BatchId!.Value;
        await WaitForAsync(f, id, b => b.Status == BenchmarkRunSeriesStatus.Stopped, "the stop");
        await WaitUntilIdleAsync(f, id);

        bool withErrors = batteryStatus == BenchmarkRunSeriesStatus.CompletedWithErrors;
        long batteryRunId;
        using (var db = CreateDbContext(f.DbName))
        {
            var batteryRun = new BenchmarkBatteryRun
            {
                BatteryName = "Battery",
                DefinitionJson = "{}",
                DefinitionSha256 = "definition-sha",
                StartRequestJson = "{}",
                RunsPerSuite = 1,
                RequestedMemberCount = 1,
                CompletedMemberCount = 1,
                Status = batteryStatus,
                StartedAtUtc = DateTime.UtcNow.AddMinutes(-5),
                CompletedAtUtc = DateTime.UtcNow,
                ErrorMessage = withErrors ? "One slot is unusable." : null
            };
            db.BenchmarkBatteryRuns.Add(batteryRun);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            batteryRunId = batteryRun.Id;

            var member = await db.BenchmarkModelBatchMembers.SingleAsync(m => m.BenchmarkModelBatchRunId == id && m.OrderIndex == 1, TestContext.Current.CancellationToken);
            member.BenchmarkRunId = null;
            member.BenchmarkBatteryRunId = batteryRunId;
            member.Status = BenchmarkModelBatchMemberStatus.Stopped;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(BenchmarkModelBatchOutcome.Ok,
            (await f.Orchestrator.ResumeAsync(id, BenchmarkModelBatchResumeMode.Continue, TestContext.Current.CancellationToken)).Outcome);

        var batch = await WaitForAsync(f, id, b => b.Status == expectedBatch, "completion");
        await WaitUntilIdleAsync(f, id);
        Assert.Equal(new long[] { 1, 2, 3 }, f.Orchestrator.Launched.ToArray());
        var adopted = batch.Members.Single(m => m.OrderIndex == 1);
        Assert.Equal(batteryRunId, adopted.BenchmarkBatteryRunId);
        Assert.Equal(expectedMember, adopted.Status);
        if (withErrors) Assert.Equal("One slot is unusable.", adopted.ErrorMessage);
        else Assert.Null(adopted.ErrorMessage);
        Assert.Null(batch.SupersededMembersJson);
    }

    // --- Reconcile after a repair -----------------------------------------------------------------

    [Fact]
    public async Task ReconcileAfterRepair_CompletesAMemberWhoseRunWasRepaired()
    {
        var f = await CreateFixtureAsync();
        f.Orchestrator.Outcome = id => id == 2 ? BenchmarkRunStatus.CompletedWithErrors : BenchmarkRunStatus.Completed;
        var result = await f.Orchestrator.StartAsync(Request(f), "admin", TestContext.Current.CancellationToken);
        long id = result.BatchId!.Value;
        var finished = await WaitForAsync(f, id, b => b.Status == BenchmarkRunSeriesStatus.CompletedWithErrors, "completion");
        await WaitUntilIdleAsync(f, id);
        Assert.NotNull(finished.StopDetail);
        long runId = finished.Members.Single(m => m.OrderIndex == 1).BenchmarkRunId!.Value;

        await FinishRunAsync(f, runId, BenchmarkRunStatus.Completed);
        await f.Orchestrator.ReconcileAfterRepairAsync(runId, TestContext.Current.CancellationToken);

        var batch = await WaitForAsync(f, id, _ => true, "the batch");
        var member = batch.Members.Single(m => m.OrderIndex == 1);
        Assert.Equal(BenchmarkModelBatchMemberStatus.Completed, member.Status);
        Assert.Null(member.ErrorMessage);
        Assert.Equal(BenchmarkRunSeriesStatus.Completed, batch.Status);
        Assert.Null(batch.StopDetail);
        Assert.Equal(3, batch.CompletedMemberCount);
    }

    [Fact]
    public async Task ReconcileAfterRepair_LeavesAStoppedMemberToContinue()
    {
        var f = await CreateFixtureAsync();
        f.Orchestrator.Outcome = id => id == 2 ? BenchmarkRunStatus.Failed : BenchmarkRunStatus.Completed;
        var result = await f.Orchestrator.StartAsync(Request(f), "admin", TestContext.Current.CancellationToken);
        long id = result.BatchId!.Value;
        var stopped = await WaitForAsync(f, id, b => b.Status == BenchmarkRunSeriesStatus.Stopped, "the stop");
        await WaitUntilIdleAsync(f, id);
        long runId = stopped.Members.Single(m => m.OrderIndex == 1).BenchmarkRunId!.Value;
        var before = stopped.Members.Single(m => m.OrderIndex == 1).Status;

        await FinishRunAsync(f, runId, BenchmarkRunStatus.Completed);
        await f.Orchestrator.ReconcileAfterRepairAsync(runId, TestContext.Current.CancellationToken);

        var batch = await WaitForAsync(f, id, _ => true, "the batch");
        Assert.Equal(before, batch.Members.Single(m => m.OrderIndex == 1).Status);
        Assert.Equal(BenchmarkRunSeriesStatus.Stopped, batch.Status);
    }

    [Fact]
    public async Task ReconcileAfterRepair_LeavesALiveBatchAlone()
    {
        var f = await CreateFixtureAsync();
        f.Orchestrator.Outcome = id => id == 1 ? BenchmarkRunStatus.Running : BenchmarkRunStatus.Completed;
        var result = await f.Orchestrator.StartAsync(Request(f), "admin", TestContext.Current.CancellationToken);
        long id = result.BatchId!.Value;
        var run = await WaitForRunOfAsync(f, 1);
        await WaitForAsync(f, id, b => b.Status == BenchmarkRunSeriesStatus.Running, "the start");

        await f.Orchestrator.ReconcileAfterRepairAsync(run.Id, TestContext.Current.CancellationToken);

        var batch = await WaitForAsync(f, id, _ => true, "the batch");
        Assert.Equal(BenchmarkModelBatchMemberStatus.Running, batch.Members.Single(m => m.OrderIndex == 0).Status);

        await f.Orchestrator.CancelAsync(id, TestContext.Current.CancellationToken);
        await WaitUntilIdleAsync(f, id);
    }

    [Fact]
    public async Task ARefusedLaunch_StopsTheBatch_WithTheMemberFailed()
    {
        var f = await CreateFixtureAsync();
        f.Orchestrator.RefuseLaunchOf.Add(1);

        var result = await f.Orchestrator.StartAsync(Request(f), "admin", TestContext.Current.CancellationToken);
        var batch = await WaitForAsync(f, result.BatchId!.Value, b => b.Status == BenchmarkRunSeriesStatus.Stopped, "the stop");

        Assert.Equal(BenchmarkModelBatchStopReason.MemberStopped, batch.StopReason);
        Assert.Contains("Refused by the test.", batch.StopDetail);
        Assert.Equal(BenchmarkModelBatchMemberStatus.Failed, batch.Members.Single(m => m.OrderIndex == 0).Status);
    }

    [Fact]
    public async Task SkipPending_WhileDriving_LeavesThatMemberOut()
    {
        var f = await CreateFixtureAsync();
        f.Orchestrator.Outcome = id => id == 1 ? BenchmarkRunStatus.Running : BenchmarkRunStatus.Completed;
        var result = await f.Orchestrator.StartAsync(Request(f), "admin", TestContext.Current.CancellationToken);
        long id = result.BatchId!.Value;
        var running = await WaitForRunOfAsync(f, 1);

        long lastMemberId;
        using (var db = CreateDbContext(f.DbName))
        {
            lastMemberId = await db.BenchmarkModelBatchMembers.Where(m => m.BenchmarkModelBatchRunId == id && m.OrderIndex == 2)
                .Select(m => m.Id).SingleAsync(TestContext.Current.CancellationToken);
        }

        Assert.Equal(BenchmarkModelBatchOutcome.Ok, (await f.Orchestrator.SkipPendingAsync(id, lastMemberId, TestContext.Current.CancellationToken)).Outcome);
        Assert.Equal(BenchmarkModelBatchOutcome.Invalid,
            (await f.Orchestrator.SkipPendingAsync(id, lastMemberId, TestContext.Current.CancellationToken)).Outcome);

        await FinishRunAsync(f, running.Id, BenchmarkRunStatus.Completed);
        var batch = await WaitForAsync(f, id, b => b.Status == BenchmarkRunSeriesStatus.CompletedWithErrors, "completion");

        Assert.Equal(new long[] { 1, 2 }, f.Orchestrator.Launched.ToArray());
        Assert.Equal(BenchmarkModelBatchMemberStatus.Skipped, batch.Members.Single(m => m.Id == lastMemberId).Status);
    }

    // --- Cancel -----------------------------------------------------------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancel_MidMember_CancelsTheChild_AndEveryPendingMember_AndReleasesTheClaim(bool childCancelThrows)
    {
        var f = await CreateFixtureAsync();
        f.Orchestrator.Outcome = _ => BenchmarkRunStatus.Running;
        f.Orchestrator.ThrowOnCancelChild = childCancelThrows;
        var result = await f.Orchestrator.StartAsync(Request(f), "admin", TestContext.Current.CancellationToken);
        long id = result.BatchId!.Value;
        await WaitForRunOfAsync(f, 1);
        await WaitForAsync(f, id, b => b.Members.Any(m => m.BenchmarkRunId != null), "the first link");
        Assert.Equal(BenchmarkRunManager.ModelBatchOwner(id), f.RunManager.BatchOwner);

        var canceled = await f.Orchestrator.CancelAsync(id, TestContext.Current.CancellationToken);

        Assert.Equal(BenchmarkModelBatchOutcome.Ok, canceled.Outcome);
        Assert.Null(f.RunManager.BatchOwner);
        Assert.Single(f.Orchestrator.CanceledMembers);

        await WaitUntilIdleAsync(f, id);
        using var db = CreateDbContext(f.DbName);
        var batch = await db.BenchmarkModelBatchRuns.Include(b => b.Members).SingleAsync(b => b.Id == id, TestContext.Current.CancellationToken);
        Assert.Equal(BenchmarkRunSeriesStatus.Cancelled, batch.Status);
        Assert.All(batch.Members, m => Assert.Equal(BenchmarkModelBatchMemberStatus.Canceled, m.Status));
        Assert.Equal(new long[] { 1 }, f.Orchestrator.Launched.ToArray());

        Assert.Equal(BenchmarkModelBatchOutcome.Invalid, (await f.Orchestrator.CancelAsync(id, TestContext.Current.CancellationToken)).Outcome);
        Assert.Equal(BenchmarkModelBatchOutcome.NotFound, (await f.Orchestrator.CancelAsync(4242, TestContext.Current.CancellationToken)).Outcome);
    }

    // --- The run-time guards ----------------------------------------------------------------------

    /// <summary>Starts a batch whose first member stays running, moves the instrument, then finishes it.</summary>
    private static async Task<long> StopOnAnInstrumentChangeAsync(Fixture f)
    {
        f.Orchestrator.Outcome = id => id == 1 ? BenchmarkRunStatus.Running : BenchmarkRunStatus.Completed;
        var result = await f.Orchestrator.StartAsync(Request(f), "admin", TestContext.Current.CancellationToken);
        long id = result.BatchId!.Value;
        var first = await WaitForRunOfAsync(f, 1);

        f.Orchestrator.Fingerprint = f.Orchestrator.Fingerprint with { ToolGuidesSha256 = "guides-2" };
        await FinishRunAsync(f, first.Id, BenchmarkRunStatus.Completed);

        var batch = await WaitForAsync(f, id, b => b.Status == BenchmarkRunSeriesStatus.Stopped, "the instrument stop");
        Assert.Equal(BenchmarkModelBatchStopReason.InstrumentChanged, batch.StopReason);
        Assert.Contains("ToolGuidesSha256", batch.StopDetail);
        Assert.Equal(new long[] { 1 }, f.Orchestrator.Launched.ToArray());

        var modes = BenchmarkModelBatchOrchestrator.ResumeOptionsFor(batch, isDriving: false).Select(o => o.Mode).ToList();
        Assert.Equal(new[] { BenchmarkModelBatchResumeMode.RerunUnderCurrentInstrument, BenchmarkModelBatchResumeMode.AcceptInstrumentChange }, modes);

        await WaitUntilIdleAsync(f, id);
        return id;
    }

    [Fact]
    public async Task AnInstrumentChange_StopsTheBatch_AndContinueIsRefused_UntilTheChangeIsAccepted()
    {
        var f = await CreateFixtureAsync();
        long id = await StopOnAnInstrumentChangeAsync(f);

        Assert.Equal(BenchmarkModelBatchOutcome.Invalid,
            (await f.Orchestrator.ResumeAsync(id, BenchmarkModelBatchResumeMode.Continue, TestContext.Current.CancellationToken)).Outcome);

        var accepted = await f.Orchestrator.ResumeAsync(id, BenchmarkModelBatchResumeMode.AcceptInstrumentChange, TestContext.Current.CancellationToken);
        Assert.Equal(BenchmarkModelBatchOutcome.Ok, accepted.Outcome);

        var batch = await WaitForAsync(f, id, b => b.Status == BenchmarkRunSeriesStatus.Completed, "completion");
        Assert.True(batch.InstrumentChangeAcknowledged);
        Assert.Equal("guides-1", batch.FirstMemberToolGuidesSha256);
        Assert.Equal(new long[] { 1, 2, 3 }, f.Orchestrator.Launched.ToArray());
    }

    // --- Replacement lineage ----------------------------------------------------------------------

    /// <summary>
    /// A single-run member Continue relaunches replaces the run it superseded, with the cause read from
    /// that run: a recorded run with a terminal answer of provider origin is a provider failure. Members
    /// launched once replace nothing.
    /// </summary>
    [Fact]
    public async Task Continue_RelaunchedSingleRunMember_ReplacesTheSupersededRun_WithAProviderFailureCause()
    {
        var ct = TestContext.Current.CancellationToken;
        var f = await CreateFixtureAsync();
        int attempts = 0;
        f.Orchestrator.Outcome = id => id == 2 && Interlocked.Increment(ref attempts) == 1
            ? BenchmarkRunStatus.Failed
            : BenchmarkRunStatus.Completed;
        var result = await f.Orchestrator.StartAsync(Request(f), "admin", ct);
        long id = result.BatchId!.Value;
        var stopped = await WaitForAsync(f, id, b => b.Status == BenchmarkRunSeriesStatus.Stopped, "the stop");
        long failedRunId = stopped.Members.Single(m => m.OrderIndex == 1).BenchmarkRunId!.Value;
        await WaitUntilIdleAsync(f, id);

        using (var db = CreateDbContext(f.DbName))
        {
            var failed = await db.BenchmarkRuns.SingleAsync(r => r.Id == failedRunId, ct);
            failed.ProviderLedgerVersion = 1;
            failed.Answers.Add(new BenchmarkRunAnswer
            {
                OrderIndex = 1,
                QuestionText = "Q1",
                AnswerText = string.Empty,
                Status = BenchmarkAnswerStatus.ProviderError,
                FailureOrigin = BenchmarkFailureOrigin.Provider
            });
            await db.SaveChangesAsync(ct);
        }

        Assert.Equal(BenchmarkModelBatchOutcome.Ok,
            (await f.Orchestrator.ResumeAsync(id, BenchmarkModelBatchResumeMode.Continue, ct)).Outcome);
        var batch = await WaitForAsync(f, id, b => b.Status == BenchmarkRunSeriesStatus.Completed, "completion");
        Assert.Equal(new long[] { 1, 2, 2, 3 }, f.Orchestrator.Launched.ToArray());
        Assert.NotEqual(failedRunId, batch.Members.Single(m => m.OrderIndex == 1).BenchmarkRunId);
        Assert.Contains($"\"runId\":{failedRunId}", batch.SupersededMembersJson);

        var relaunched = await WaitForRunOfAsync(f, 2, nth: 2);
        Assert.Equal(failedRunId, relaunched.ReplacesRunId);
        Assert.Equal(BenchmarkReplacementCause.ProviderFailure, relaunched.ReplacementCause);

        Assert.Null((await WaitForRunOfAsync(f, 1)).ReplacesRunId);
        Assert.Null((await WaitForRunOfAsync(f, 3)).ReplacesRunId);
        Assert.Equal(new long?[] { null, null, failedRunId, null }, f.Orchestrator.ReplacesOf.Select(r => r.Replaces?.Run?.RunId));
    }

    /// <summary>Re-running under the current instrument links the relaunched member to its old run as an instrument change.</summary>
    [Fact]
    public async Task RerunUnderCurrentInstrument_RelaunchedMember_ReplacesItsRun_AsAnInstrumentChange()
    {
        var ct = TestContext.Current.CancellationToken;
        var f = await CreateFixtureAsync();
        long id = await StopOnAnInstrumentChangeAsync(f);
        long firstRunId = (await WaitForRunOfAsync(f, 1)).Id;
        f.Orchestrator.Outcome = _ => BenchmarkRunStatus.Completed;

        Assert.Equal(BenchmarkModelBatchOutcome.Ok,
            (await f.Orchestrator.ResumeAsync(id, BenchmarkModelBatchResumeMode.RerunUnderCurrentInstrument, ct)).Outcome);
        var batch = await WaitForAsync(f, id, b => b.Status == BenchmarkRunSeriesStatus.Completed, "completion");
        Assert.Equal(new long[] { 1, 1, 2, 3 }, f.Orchestrator.Launched.ToArray());
        Assert.Equal("guides-2", batch.FirstMemberToolGuidesSha256);
        Assert.False(batch.InstrumentChangeAcknowledged);
        Assert.NotNull(batch.SupersededMembersJson);
        Assert.Single(JsonDocument.Parse(batch.SupersededMembersJson!).RootElement.EnumerateArray());

        var rerun = await WaitForRunOfAsync(f, 1, nth: 2);
        Assert.Equal(firstRunId, rerun.ReplacesRunId);
        Assert.Equal(BenchmarkReplacementCause.InstrumentChange, rerun.ReplacementCause);
        Assert.Contains("\"instrumentRerun\":true", batch.SupersededMembersJson);
        Assert.Null((await WaitForRunOfAsync(f, 2)).ReplacesRunId);
    }

    /// <summary>The batch's superseded record as <c>SupersedeLinks</c> writes it, oldest first.</summary>
    private static string SupersededLinksJson(
        params (long MemberId, long? RunId, long? SeriesId, long? BatteryRunId, bool InstrumentRerun)[] links)
        => JsonSerializer.Serialize(links.Select(l => new
        {
            memberId = l.MemberId,
            orderIndex = 0,
            testedModelConfigurationId = 1L,
            status = "Failed",
            runId = l.RunId,
            seriesId = l.SeriesId,
            batteryRunId = l.BatteryRunId,
            supersededAtUtc = DateTime.UtcNow,
            instrumentRerun = l.InstrumentRerun
        }));

    private static BenchmarkRun LineageRun(long suiteId, BenchmarkRunStatus status, bool providerFailure = false)
    {
        var run = BenchmarkModelSnapshots.Attach(new BenchmarkRun
        {
            BenchmarkSuiteId = suiteId,
            SuiteName = "Suite A",
            Status = status,
            StartedAtUtc = DateTime.UtcNow,
            ProviderLedgerVersion = 1
        });
        if (providerFailure)
        {
            run.Answers.Add(new BenchmarkRunAnswer
            {
                OrderIndex = 1,
                QuestionText = "Q1",
                AnswerText = string.Empty,
                Status = BenchmarkAnswerStatus.ProviderError,
                FailureOrigin = BenchmarkFailureOrigin.Provider
            });
        }

        return run;
    }

    /// <summary>
    /// A superseded series child is linked member to member: each index of the new series replaces the
    /// newest run at that index of the old one, each with its own cause. The member's newest link wins.
    /// </summary>
    [Fact]
    public async Task ChildReplacement_OfASupersededSeries_LinksEachIndexToTheOldSeriesRunAtThatIndex()
    {
        var ct = TestContext.Current.CancellationToken;
        var f = await CreateFixtureAsync();
        using var db = CreateDbContext(f.DbName);

        var series = new BenchmarkRunSeries
        {
            BenchmarkSuiteId = f.SuiteId,
            SuiteName = "Suite A",
            RequestedRunCount = 3,
            Status = BenchmarkRunSeriesStatus.Stopped,
            StartRequestJson = "{}",
            StartedAtUtc = DateTime.UtcNow
        };
        db.BenchmarkRunSeries.Add(series);
        await db.SaveChangesAsync(ct);

        var one = LineageRun(f.SuiteId, BenchmarkRunStatus.Completed);
        var two = LineageRun(f.SuiteId, BenchmarkRunStatus.Failed, providerFailure: true);
        var three = LineageRun(f.SuiteId, BenchmarkRunStatus.Canceled);
        foreach (var (run, index) in new[] { (one, 1), (two, 2), (three, 3) })
        {
            run.RunSeriesId = series.Id;
            run.RunSeriesIndex = index;
            db.BenchmarkRuns.Add(run);
        }
        await db.SaveChangesAsync(ct);

        var batch = new BenchmarkModelBatchRun
        {
            SupersededMembersJson = SupersededLinksJson((7, 4242, null, null, false), (7, null, series.Id, null, false), (8, 4343, null, null, false))
        };

        var replaces = await BenchmarkModelBatchOrchestrator.ChildReplacementAsync(db, batch, new BenchmarkModelBatchMember { Id = 7 }, ct);

        Assert.NotNull(replaces);
        Assert.Null(replaces!.Run);
        Assert.Null(replaces.BatteryMembers);
        var members = replaces.SeriesMembers!;
        Assert.Equal(3, members.Count);
        Assert.Equal(new BenchmarkRunReplacement(one.Id, BenchmarkReplacementCause.OurFailure), members[1]);
        Assert.Equal(new BenchmarkRunReplacement(two.Id, BenchmarkReplacementCause.ProviderFailure), members[2]);
        Assert.Equal(new BenchmarkRunReplacement(three.Id, BenchmarkReplacementCause.OperatorRequest), members[3]);

        Assert.Null(await BenchmarkModelBatchOrchestrator.ChildReplacementAsync(db, batch, new BenchmarkModelBatchMember { Id = 9 }, ct));
    }

    /// <summary>
    /// A superseded battery child is linked member to member by slot: the slot's live member, else its
    /// newest superseded one. Under a re-run every link is an instrument change.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChildReplacement_OfASupersededBatteryRun_LinksEachSlotToItsMemberRun(bool instrumentRerun)
    {
        var ct = TestContext.Current.CancellationToken;
        var f = await CreateFixtureAsync();
        using var db = CreateDbContext(f.DbName);

        var batteryRun = new BenchmarkBatteryRun
        {
            BatteryName = "Battery",
            DefinitionJson = "{}",
            DefinitionSha256 = "definition-sha",
            StartRequestJson = "{}",
            RunsPerSuite = 1,
            RequestedMemberCount = 2,
            Status = BenchmarkRunSeriesStatus.Stopped,
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-5)
        };
        db.BenchmarkBatteryRuns.Add(batteryRun);

        var replacedEarlier = LineageRun(f.SuiteId, BenchmarkRunStatus.Failed);
        var live = LineageRun(f.SuiteId, BenchmarkRunStatus.Completed);
        var lastAttempt = LineageRun(f.SuiteId, BenchmarkRunStatus.Failed, providerFailure: true);
        db.BenchmarkRuns.AddRange(replacedEarlier, live, lastAttempt);
        await db.SaveChangesAsync(ct);

        var start = DateTime.UtcNow.AddMinutes(-4);
        db.BenchmarkBatteryRunMembers.AddRange(
            new BenchmarkBatteryRunMember { BenchmarkBatteryRunId = batteryRun.Id, BenchmarkRunId = replacedEarlier.Id, SuiteIndex = 0, Round = 1, Superseded = true, AddedAtUtc = start },
            new BenchmarkBatteryRunMember { BenchmarkBatteryRunId = batteryRun.Id, BenchmarkRunId = live.Id, SuiteIndex = 0, Round = 1, AddedAtUtc = start.AddMinutes(1) },
            new BenchmarkBatteryRunMember { BenchmarkBatteryRunId = batteryRun.Id, BenchmarkRunId = lastAttempt.Id, SuiteIndex = 1, Round = 1, Superseded = true, AddedAtUtc = start.AddMinutes(2) });
        await db.SaveChangesAsync(ct);

        var batch = new BenchmarkModelBatchRun
        {
            SupersededMembersJson = SupersededLinksJson((7, null, null, batteryRun.Id, instrumentRerun))
        };

        var replaces = await BenchmarkModelBatchOrchestrator.ChildReplacementAsync(db, batch, new BenchmarkModelBatchMember { Id = 7 }, ct);

        var members = replaces!.BatteryMembers!;
        Assert.Null(replaces.Run);
        Assert.Null(replaces.SeriesMembers);
        Assert.Equal(2, members.Count);
        Assert.Equal(live.Id, members[(0, 1)].RunId);
        Assert.Equal(lastAttempt.Id, members[(1, 1)].RunId);
        Assert.Equal(instrumentRerun ? BenchmarkReplacementCause.InstrumentChange : BenchmarkReplacementCause.OurFailure, members[(0, 1)].Cause);
        Assert.Equal(instrumentRerun ? BenchmarkReplacementCause.InstrumentChange : BenchmarkReplacementCause.ProviderFailure, members[(1, 1)].Cause);
    }

    [Fact]
    public async Task AGraderConfigurationChange_BetweenMembers_StopsTheBatch()
    {
        var f = await CreateFixtureAsync();
        f.Orchestrator.AssessorOf = id => id == 2
            ? BenchmarkModelSnapshots.Model(provider: "OpenAI", modelId: "gpt-5-edited", displayName: "Edited Assessor")
            : null;

        var result = await f.Orchestrator.StartAsync(Request(f), "admin", TestContext.Current.CancellationToken);
        var batch = await WaitForAsync(f, result.BatchId!.Value, b => b.Status == BenchmarkRunSeriesStatus.Stopped, "the grader stop");

        Assert.Equal(BenchmarkModelBatchStopReason.GraderConfigChanged, batch.StopReason);
        Assert.Contains(BenchmarkComparabilityKey.AssessorConfigurationKey, batch.StopDetail);
        Assert.Equal(new long[] { 1, 2 }, f.Orchestrator.Launched.ToArray());
    }

    // --- Resume refusals and startup reconciliation -----------------------------------------------

    [Fact]
    public async Task Resume_IsRefused_ForABatchThatIsNotStopped()
    {
        var f = await CreateFixtureAsync();
        var result = await f.Orchestrator.StartAsync(Request(f), "admin", TestContext.Current.CancellationToken);
        long id = result.BatchId!.Value;
        await WaitForAsync(f, id, b => b.Status == BenchmarkRunSeriesStatus.Completed, "completion");
        await WaitUntilIdleAsync(f, id);

        Assert.Equal(BenchmarkModelBatchOutcome.Invalid,
            (await f.Orchestrator.ResumeAsync(id, BenchmarkModelBatchResumeMode.Continue, TestContext.Current.CancellationToken)).Outcome);
        Assert.Equal(BenchmarkModelBatchOutcome.NotFound,
            (await f.Orchestrator.ResumeAsync(4242, BenchmarkModelBatchResumeMode.Continue, TestContext.Current.CancellationToken)).Outcome);
    }

    [Fact]
    public async Task ReconcileOrphaned_StopsLiveBatches_WithTheirRunningMember()
    {
        var f = await CreateFixtureAsync();
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateDbContext(f.DbName);

        BenchmarkModelBatchRun Batch(BenchmarkRunSeriesStatus status) => new()
        {
            StartRequestJson = "{}",
            Status = status,
            Members =
            {
                new BenchmarkModelBatchMember { OrderIndex = 0, TestedModelConfigurationId = 1, Status = BenchmarkModelBatchMemberStatus.Completed },
                new BenchmarkModelBatchMember { OrderIndex = 1, TestedModelConfigurationId = 2, Status = BenchmarkModelBatchMemberStatus.Running },
                new BenchmarkModelBatchMember { OrderIndex = 2, TestedModelConfigurationId = 3, Status = BenchmarkModelBatchMemberStatus.Pending }
            }
        };

        db.BenchmarkModelBatchRuns.AddRange(
            Batch(BenchmarkRunSeriesStatus.Running),
            Batch(BenchmarkRunSeriesStatus.Pending),
            Batch(BenchmarkRunSeriesStatus.WaitingForCap),
            Batch(BenchmarkRunSeriesStatus.Completed));
        await db.SaveChangesAsync(ct);

        Assert.Equal(3, await f.Orchestrator.ReconcileOrphanedAsync(db, ct));

        using var readback = CreateDbContext(f.DbName);
        var batches = await readback.BenchmarkModelBatchRuns.Include(b => b.Members).ToListAsync(ct);
        var stopped = batches.Where(b => b.Status == BenchmarkRunSeriesStatus.Stopped).ToList();
        Assert.Equal(3, stopped.Count);
        Assert.All(stopped, b => Assert.Equal(BenchmarkModelBatchStopReason.RestartReconciled, b.StopReason));
        Assert.All(stopped, b => Assert.Equal(BenchmarkModelBatchMemberStatus.Stopped, b.Members.Single(m => m.OrderIndex == 1).Status));
        Assert.Single(batches, b => b.Status == BenchmarkRunSeriesStatus.Completed);

        var modes = BenchmarkModelBatchOrchestrator.ResumeOptionsFor(stopped[0], isDriving: false).Select(o => o.Mode).ToList();
        Assert.Equal(new[] { BenchmarkModelBatchResumeMode.Continue, BenchmarkModelBatchResumeMode.SkipCurrent }, modes);

        Assert.Equal(0, await f.Orchestrator.ReconcileOrphanedAsync(readback, ct));
    }

    // --- The run cap ------------------------------------------------------------------------------

    private static async Task<long> FillHourlyCapAsync(Fixture f, int runs)
    {
        using var db = CreateDbContext(f.DbName);
        long last = 0;
        for (int i = 0; i < runs; i++)
        {
            var run = BenchmarkModelSnapshots.Attach(new BenchmarkRun
            {
                BenchmarkSuiteId = f.SuiteId,
                SuiteName = "Suite A",
                Status = BenchmarkRunStatus.Completed,
                StartedAtUtc = DateTime.UtcNow.AddMinutes(-5)
            });
            db.BenchmarkRuns.Add(run);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            last = run.Id;
        }
        return last;
    }

    [Fact]
    public async Task TheRunCap_StopsTheBatchBeforeAnyLaunch_WithoutWait()
    {
        var f = await CreateFixtureAsync(maxRunsPerHour: 1);
        await FillHourlyCapAsync(f, 1);

        var result = await f.Orchestrator.StartAsync(Request(f), "admin", TestContext.Current.CancellationToken);
        var batch = await WaitForAsync(f, result.BatchId!.Value, b => b.Status == BenchmarkRunSeriesStatus.Stopped, "the cap stop");

        Assert.Equal(BenchmarkModelBatchStopReason.RunCapReached, batch.StopReason);
        Assert.Empty(f.Orchestrator.Launched);
        Assert.All(batch.Members, m => Assert.Equal(BenchmarkModelBatchMemberStatus.Pending, m.Status));
        await WaitUntilIdleAsync(f, batch.Id);
    }

    [Fact]
    public async Task TheRunCap_IsWaitedOn_WithWait_AndTheBatchGoesOnWhenItLifts()
    {
        var f = await CreateFixtureAsync(maxRunsPerHour: 3);
        await FillHourlyCapAsync(f, 3);

        var result = await f.Orchestrator.StartAsync(Request(f, allowCapWait: true, ids: new long[] { 1, 2 }), "admin", TestContext.Current.CancellationToken);
        long id = result.BatchId!.Value;
        await WaitForAsync(f, id, b => b.Status == BenchmarkRunSeriesStatus.WaitingForCap, "the cap wait");
        Assert.Empty(f.Orchestrator.Launched);

        using (var db = CreateDbContext(f.DbName))
        {
            db.BenchmarkRuns.RemoveRange(await db.BenchmarkRuns.ToListAsync(TestContext.Current.CancellationToken));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await WaitForAsync(f, id, b => b.Status == BenchmarkRunSeriesStatus.Completed, "completion");
        Assert.Equal(new long[] { 1, 2 }, f.Orchestrator.Launched.ToArray());
    }

    [Fact]
    public async Task ACapWaitThatOutlastsItsBudget_StopsTheBatch()
    {
        var f = await CreateFixtureAsync(maxRunsPerHour: 1);
        await FillHourlyCapAsync(f, 1);
        f.Orchestrator.CapWaitBudget = TimeSpan.FromMilliseconds(50);

        var result = await f.Orchestrator.StartAsync(Request(f, allowCapWait: true), "admin", TestContext.Current.CancellationToken);
        var batch = await WaitForAsync(f, result.BatchId!.Value, b => b.Status == BenchmarkRunSeriesStatus.Stopped, "the budget stop");

        Assert.Equal(BenchmarkModelBatchStopReason.RunCapReached, batch.StopReason);
        Assert.Contains("budget", batch.StopDetail);
        Assert.Empty(f.Orchestrator.Launched);
    }
}
