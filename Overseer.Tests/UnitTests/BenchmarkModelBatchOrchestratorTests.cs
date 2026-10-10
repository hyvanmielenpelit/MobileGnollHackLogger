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
            CancellationToken ct)
        {
            long configId = member.TestedModelConfigurationId;
            Launched.Enqueue(configId);
            Requests[configId] = request;

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
                ErrorMessage = status == BenchmarkRunStatus.Failed ? "The provider failed." : null
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
    public async Task Continue_RelaunchesAFailedSingleRunMember_KeepingTheOldRunAsSuperseded()
    {
        var f = await CreateFixtureAsync();
        int attempts = 0;
        f.Orchestrator.Outcome = id => id == 2 && Interlocked.Increment(ref attempts) == 1
            ? BenchmarkRunStatus.Failed
            : BenchmarkRunStatus.Completed;
        var result = await f.Orchestrator.StartAsync(Request(f), "admin", TestContext.Current.CancellationToken);
        long id = result.BatchId!.Value;
        var stopped = await WaitForAsync(f, id, b => b.Status == BenchmarkRunSeriesStatus.Stopped, "the stop");
        long failedRunId = stopped.Members.Single(m => m.OrderIndex == 1).BenchmarkRunId!.Value;
        await WaitUntilIdleAsync(f, id);

        Assert.Equal(BenchmarkModelBatchOutcome.Ok,
            (await f.Orchestrator.ResumeAsync(id, BenchmarkModelBatchResumeMode.Continue, TestContext.Current.CancellationToken)).Outcome);

        var batch = await WaitForAsync(f, id, b => b.Status == BenchmarkRunSeriesStatus.Completed, "completion");
        Assert.Equal(new long[] { 1, 2, 2, 3 }, f.Orchestrator.Launched.ToArray());
        Assert.NotEqual(failedRunId, batch.Members.Single(m => m.OrderIndex == 1).BenchmarkRunId);
        Assert.Contains($"\"runId\":{failedRunId}", batch.SupersededMembersJson);
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

    [Fact]
    public async Task AnInstrumentChange_RerunUnderCurrentInstrument_StartsOver_KeepingTheOldLinks()
    {
        var f = await CreateFixtureAsync();
        long id = await StopOnAnInstrumentChangeAsync(f);
        f.Orchestrator.Outcome = _ => BenchmarkRunStatus.Completed;

        var rerun = await f.Orchestrator.ResumeAsync(id, BenchmarkModelBatchResumeMode.RerunUnderCurrentInstrument, TestContext.Current.CancellationToken);
        Assert.Equal(BenchmarkModelBatchOutcome.Ok, rerun.Outcome);

        var batch = await WaitForAsync(f, id, b => b.Status == BenchmarkRunSeriesStatus.Completed, "completion");
        Assert.Equal(new long[] { 1, 1, 2, 3 }, f.Orchestrator.Launched.ToArray());
        Assert.Equal("guides-2", batch.FirstMemberToolGuidesSha256);
        Assert.False(batch.InstrumentChangeAcknowledged);
        Assert.NotNull(batch.SupersededMembersJson);
        Assert.Single(JsonDocument.Parse(batch.SupersededMembersJson!).RootElement.EnumerateArray());
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
