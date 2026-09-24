namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Services;
using Overseer.Services.Benchmarking;
using Overseer.Tests.Helpers;
using Xunit;

/// <summary>
/// A single-answer re-run records its own instrument and span in the <c>Rerun*</c> columns, as the
/// failed-question re-run does, and leaves the fingerprints and end time the run was made with.
///
/// <para>The test service has no <c>ChatService</c> and no provider, so no re-run reaches the
/// candidate here. <see cref="BenchmarkService.BeginRerun"/> carries the writes both re-run paths
/// make when they start, and is tested directly; the service tests cover what the single-answer
/// re-run's refusal, error and cancellation paths leave behind.</para>
/// </summary>
public class BenchmarkSingleAnswerRerunProvenanceTests
{
    private static readonly DateTime CompletedAt = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
    private const string OriginalPromptSha = "0000000000000000000000000000000000000000000000000000000000000001";
    private const string OriginalPromptText = "The prompt the run was made with.";
    private const string OriginalGuidesSha = "0000000000000000000000000000000000000000000000000000000000000002";

    private static (BenchmarkService Service, BenchmarkRunManager RunManager) CreateService(
        DbContextOptions<ApplicationDbContext> dbOptions)
    {
        var runManager = new BenchmarkRunManager();
        var config = BenchmarkComplianceGuardTests.CreateConfig(maxRunsPerHour: 10);

        var services = new ServiceCollection();
        services.AddScoped(_ => new ApplicationDbContext(dbOptions));
        services.AddScoped<SystemAiConfigService>();
        services.AddSingleton<ILogger<SystemAiConfigService>>(NullLogger<SystemAiConfigService>.Instance);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var cryptoService = new CryptoService(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "AesEncryptionKey", Convert.ToBase64String(new byte[32]) }
        }).Build());

        var service = new BenchmarkService(
            scopeFactory,
            null!,
            null!,
            cryptoService,
            runManager,
            new BenchmarkDifficultyJobManager(),
            new BenchmarkScoringProfileService(scopeFactory, NullLogger<BenchmarkScoringProfileService>.Instance),
            new Overseer.Services.Privacy.EndpointPolicy(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build()),
            config,
            NullLogger<BenchmarkService>.Instance);

        return (service, runManager);
    }

    private static BenchmarkRun CompletedRun() => BenchmarkModelSnapshots.Attach(new BenchmarkRun
    {
        SuiteName = "Suite",
        Status = BenchmarkRunStatus.Completed,
        StartedAtUtc = CompletedAt.AddHours(-1),
        CompletedAtUtc = CompletedAt,
        ScoringMethodVersion = BenchmarkAssessmentPrompt.ScoringMethodVersion,
        HarnessVersion = "1",
        CandidatePromptOptionsJson = "{}",
        CandidateSystemPromptSha256 = OriginalPromptSha,
        CandidateSystemPromptText = OriginalPromptText,
        ToolGuidesSha256 = OriginalGuidesSha,
        KnowledgeBaseHeadSha = "kb-head",
        WikiHeadSha = "wiki-head",
        SourceCodeHeadSha = "source-head",
        TotalQuestionCount = 1
    });

    private static void AssertOriginalProvenance(BenchmarkRun run)
    {
        Assert.Equal(OriginalPromptSha, run.CandidateSystemPromptSha256);
        Assert.Equal(OriginalPromptText, run.CandidateSystemPromptText);
        Assert.Equal(OriginalGuidesSha, run.ToolGuidesSha256);
        Assert.Equal("kb-head", run.KnowledgeBaseHeadSha);
        Assert.Equal("wiki-head", run.WikiHeadSha);
        Assert.Equal("source-head", run.SourceCodeHeadSha);
        Assert.Equal(CompletedAt, run.CompletedAtUtc);
    }

    [Fact]
    public void BeginRerun_RecordsTheReRunsInstrumentAndSpan_AndLeavesTheRunsOwn()
    {
        var (service, _) = CreateService(BenchmarkRunExamTests.InMemoryOptions());
        var run = CompletedRun();
        run.RerunCompletedAtUtc = CompletedAt.AddDays(-1);

        service.BeginRerun(run, "A prompt that has changed since the run.");

        AssertOriginalProvenance(run);
        Assert.Equal(BenchmarkRunStatus.Running, run.Status);
        Assert.NotNull(run.RerunCandidateSystemPromptSha256);
        Assert.NotEqual(OriginalPromptSha, run.RerunCandidateSystemPromptSha256);
        Assert.Equal(BenchmarkAssessmentPrompt.HarnessVersion, run.RerunHarnessVersion);
        Assert.Equal(BenchmarkService.ComputeToolGuidesSha256(), run.RerunToolGuidesSha256);
        Assert.NotNull(run.RerunStartedAtUtc);
        Assert.Null(run.RerunCompletedAtUtc);
    }

    private static async Task<(long RunId, long AnswerId)> SeedAsync(
        DbContextOptions<ApplicationDbContext> options, Action<BenchmarkRun>? adjust = null)
    {
        await using var db = new ApplicationDbContext(options);
        var (suite, modelA, _, modelC) = await BenchmarkComplianceGuardTests.SeedConfigsAndSuite(db);

        var run = CompletedRun();
        run.BenchmarkSuiteId = suite.Id;
        run.TestedModelConfigurationId = modelA.Id;
        run.TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: modelA.Provider!, modelId: modelA.ModelId!, displayName: modelA.DisplayName!);
        run.AssessorModelConfigurationId = modelC.Id;
        run.AssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: modelC.Provider!, modelId: modelC.ModelId!, displayName: modelC.DisplayName!);
        run.Answers.Add(new BenchmarkRunAnswer
        {
            OrderIndex = 1,
            QuestionText = "Q1",
            AnswerText = "A1",
            Status = BenchmarkAnswerStatus.Ok,
            AssessmentStatus = BenchmarkAssessmentStatus.Scored,
            QualityScore = 70,
            ExpectedPointsUsed = "- point",
            ExpectedPointsRecorded = true
        });
        adjust?.Invoke(run);

        // As the controller leaves the row before the background task starts.
        run.Status = BenchmarkRunStatus.Running;
        run.RerunStartedAtUtc = DateTime.UtcNow;
        db.BenchmarkRuns.Add(run);
        await db.SaveChangesAsync();
        return (run.Id, run.Answers.Single().Id);
    }

    private static async Task<BenchmarkRun> ReloadAsync(DbContextOptions<ApplicationDbContext> options, long runId)
    {
        await using var db = new ApplicationDbContext(options);
        return await db.BenchmarkRuns.Include(r => r.Answers).FirstAsync(r => r.Id == runId);
    }

    [Fact]
    public async Task ARefusedSingleAnswerReRun_LeavesTheRunsEndTimeAndFingerprints()
    {
        var options = BenchmarkRunExamTests.InMemoryOptions();
        var (service, runManager) = CreateService(options);
        var (runId, answerId) = await SeedAsync(options, run => run.ScoringMethodVersion = 0);

        Assert.True(runManager.TryStart(runId, new CancellationTokenSource(), out _));
        await service.RerunSingleQuestionAsync(runId, answerId, null, CancellationToken.None);

        var reloaded = await ReloadAsync(options, runId);
        AssertOriginalProvenance(reloaded);
        Assert.Null(reloaded.RerunCandidateSystemPromptSha256);
        Assert.Equal(BenchmarkService.ScoringMethodRefusal(reloaded), reloaded.ErrorMessage);
    }

    [Fact]
    public async Task ASingleAnswerReRunThatFails_KeepsTheRunsEndTime_AndClosesItsOwnSpan()
    {
        // The test configurations carry keys that do not decrypt, so the re-run throws after its
        // refusals: the general error path.
        var options = BenchmarkRunExamTests.InMemoryOptions();
        var (service, runManager) = CreateService(options);
        var (runId, answerId) = await SeedAsync(options);

        Assert.True(runManager.TryStart(runId, new CancellationTokenSource(), out _));
        await service.RerunSingleQuestionAsync(runId, answerId, null, CancellationToken.None);

        var reloaded = await ReloadAsync(options, runId);
        AssertOriginalProvenance(reloaded);
        Assert.Equal(BenchmarkRunStatus.CompletedWithErrors, reloaded.Status);
        Assert.NotNull(reloaded.RerunCompletedAtUtc);
        Assert.Null(runManager.CurrentRunId);
    }

    [Fact]
    public async Task ACanceledSingleAnswerReRun_KeepsTheRunsEndTime_AndClosesItsOwnSpan()
    {
        var options = BenchmarkRunExamTests.InMemoryOptions();
        var (service, runManager) = CreateService(options);
        var (runId, answerId) = await SeedAsync(options);

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.True(runManager.TryStart(runId, new CancellationTokenSource(), out _));
        await service.RerunSingleQuestionAsync(runId, answerId, null, canceled.Token);

        var reloaded = await ReloadAsync(options, runId);
        AssertOriginalProvenance(reloaded);
        Assert.NotEqual(BenchmarkRunStatus.Running, reloaded.Status);
        Assert.NotNull(reloaded.RerunCompletedAtUtc);
        Assert.Equal("Answer re-run canceled.", reloaded.ErrorMessage);
    }
}
