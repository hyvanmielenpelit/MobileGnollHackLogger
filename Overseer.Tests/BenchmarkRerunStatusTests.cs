namespace Overseer.Tests;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.Benchmarking;
using Xunit;

/// <summary>
/// Covers the fix for the six re-run/re-assess controller endpoints that used to leave a run's
/// <c>Status</c> reading its previous terminal value until the background task got around to
/// flipping it: the client polls the run detail the instant <c>Accepted</c> comes back, saw the
/// stale terminal status, and stopped polling as if the run had already finished. The controller
/// now flips the row to <c>Running</c> synchronously, before <c>Task.Run</c> is even scheduled.
///
/// Every controller test below asserts on the <b>tracked</b> <c>run</c> instance returned by
/// <c>SeedConfigsAndSuite</c>'s caller, not on a freshly-queried row. The background <c>Task.Run</c>
/// each endpoint fires uses its own scoped <c>ApplicationDbContext</c> and will shortly flip the
/// row again — to a terminal status, since the seeded model configurations carry a dummy encrypted
/// key that cannot be decrypted — so a fresh query races that background task and is not a
/// reliable way to observe what the controller itself wrote. The tracked instance already holds
/// the synchronous write under test, made before that background task was ever scheduled.
/// </summary>
public class BenchmarkRerunStatusTests
{
    /// <summary>
    /// Builds a <see cref="BenchmarkService"/> and its <see cref="BenchmarkRunManager"/> directly,
    /// on the same in-memory database as <paramref name="dbOptions"/>, for the two service-level
    /// tests below. Duplicated from <see cref="BenchmarkComplianceGuardTests.CreateTestBenchmarkController"/>
    /// rather than folded into it: that fixture's return tuple is destructured by every existing
    /// test in this assembly, and widening it here would touch all of them for two tests' benefit.
    /// </summary>
    private static (BenchmarkService service, BenchmarkRunManager runManager) CreateTestBenchmarkService(
        DbContextOptions<ApplicationDbContext> dbOptions, IConfiguration config)
    {
        var runManager = new BenchmarkRunManager();

        var services = new ServiceCollection();
        services.AddScoped(_ => new ApplicationDbContext(dbOptions));
        services.AddScoped<SystemAiConfigService>();
        // Unlike CreateTestBenchmarkController's fire-and-forget Task.Run, these two tests await
        // the service method directly, so SystemAiConfigService's constructor dependency has to
        // resolve rather than fail silently on an abandoned background task.
        services.AddSingleton<ILogger<SystemAiConfigService>>(NullLogger<SystemAiConfigService>.Instance);
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        var scoringProfileService = new BenchmarkScoringProfileService(scopeFactory, NullLogger<BenchmarkScoringProfileService>.Instance);

        var cryptoService = new CryptoService(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "AesEncryptionKey", Convert.ToBase64String(new byte[32]) }
        }).Build());

        var difficultyJobManager = new BenchmarkDifficultyJobManager();

        var benchmarkService = new BenchmarkService(
            scopeFactory,
            null!,
            null!,
            cryptoService,
            runManager,
            difficultyJobManager,
            scoringProfileService,
            config,
            NullLogger<BenchmarkService>.Instance);

        return (benchmarkService, runManager);
    }

    private static BenchmarkRun BuildSeedRun(BenchmarkSuite suite, SystemAiApiConfiguration modelA, SystemAiApiConfiguration modelC)
    {
        return new BenchmarkRun
        {
            Status = BenchmarkRunStatus.CompletedWithErrors,
            BenchmarkSuiteId = suite.Id,
            SuiteName = suite.Name,
            TestedModelConfigurationId = modelA.Id,
            TestedModelProviderUsed = modelA.Provider!,
            TestedModelIdUsed = modelA.ModelId!,
            TestedModelDisplayNameUsed = modelA.DisplayName!,
            AssessorModelConfigurationId = modelC.Id,
            AssessorModelProviderUsed = modelC.Provider!,
            AssessorModelIdUsed = modelC.ModelId!,
            AssessorModelDisplayNameUsed = modelC.DisplayName!,
            ClaimVerifierModelConfigurationId = modelC.Id,
            StartedAtUtc = DateTime.UtcNow.AddHours(-2),
            CompletedAtUtc = DateTime.UtcNow.AddHours(-1)
        };
    }

    [Fact]
    public async Task RerunFailedQuestions_MarksRunRunning_BeforeReturningAccepted()
    {
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController(maxRunsPerHour: 10);
        var (suite, modelA, _, modelC) = await BenchmarkComplianceGuardTests.SeedConfigsAndSuite(db);

        var run = BuildSeedRun(suite, modelA, modelC);
        run.ErrorMessage = "previous attempt";
        run.Answers.Add(new BenchmarkRunAnswer
        {
            QuestionText = "Q1",
            AnswerText = "A1",
            Status = BenchmarkAnswerStatus.ProviderError,
            OrderIndex = 1
        });
        db.BenchmarkRuns.Add(run);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await controller.RerunFailedQuestions(run.Id);

        Assert.IsType<AcceptedResult>(result);
        Assert.Equal(BenchmarkRunStatus.Running, run.Status);
        Assert.NotNull(run.RerunStartedAtUtc);
        Assert.Null(run.ErrorMessage);
    }

    [Fact]
    public async Task RerunAnswer_MarksRunRunning_BeforeReturningAccepted()
    {
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController(maxRunsPerHour: 10);
        var (suite, modelA, _, modelC) = await BenchmarkComplianceGuardTests.SeedConfigsAndSuite(db);

        var run = BuildSeedRun(suite, modelA, modelC);
        var answer = new BenchmarkRunAnswer
        {
            QuestionText = "Q1",
            AnswerText = "A1",
            Status = BenchmarkAnswerStatus.Ok,
            OrderIndex = 1
        };
        run.Answers.Add(answer);
        db.BenchmarkRuns.Add(run);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await controller.RerunAnswer(run.Id, answer.Id, new BenchmarkRetryRequest());

        Assert.IsType<AcceptedResult>(result);
        Assert.Equal(BenchmarkRunStatus.Running, run.Status);
    }

    [Fact]
    public async Task RerunSynthesis_MarksRunRunning_BeforeReturningAccepted()
    {
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController(maxRunsPerHour: 10);
        var (suite, modelA, _, modelC) = await BenchmarkComplianceGuardTests.SeedConfigsAndSuite(db);

        var run = BuildSeedRun(suite, modelA, modelC);
        run.Answers.Add(new BenchmarkRunAnswer
        {
            QuestionText = "Q1",
            AnswerText = "A1",
            Status = BenchmarkAnswerStatus.Ok,
            OrderIndex = 1
        });
        db.BenchmarkRuns.Add(run);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await controller.RerunSynthesis(run.Id, null);

        Assert.IsType<AcceptedResult>(result);
        Assert.Equal(BenchmarkRunStatus.Running, run.Status);
    }

    [Fact]
    public async Task RetryFailedAssessments_MarksRunRunning_BeforeReturningAccepted()
    {
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController(maxRunsPerHour: 10);
        var (suite, modelA, _, modelC) = await BenchmarkComplianceGuardTests.SeedConfigsAndSuite(db);

        var run = BuildSeedRun(suite, modelA, modelC);
        run.Answers.Add(new BenchmarkRunAnswer
        {
            QuestionText = "Q1",
            AnswerText = "A1",
            Status = BenchmarkAnswerStatus.Ok,
            AssessmentStatus = BenchmarkAssessmentStatus.Pending,
            OrderIndex = 1
        });
        db.BenchmarkRuns.Add(run);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await controller.RetryFailedAssessments(run.Id, null);

        Assert.IsType<AcceptedResult>(result);
        Assert.Equal(BenchmarkRunStatus.Running, run.Status);
    }

    [Fact]
    public async Task RetryClaimVerification_MarksRunRunning_BeforeReturningAccepted()
    {
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController(maxRunsPerHour: 10);
        var (suite, modelA, _, modelC) = await BenchmarkComplianceGuardTests.SeedConfigsAndSuite(db);

        var run = BuildSeedRun(suite, modelA, modelC);
        run.Answers.Add(new BenchmarkRunAnswer
        {
            QuestionText = "Q1",
            AnswerText = "A1",
            Status = BenchmarkAnswerStatus.Ok,
            ClaimVerificationError = "timeout",
            OrderIndex = 1
        });
        db.BenchmarkRuns.Add(run);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await controller.RetryClaimVerification(run.Id, null);

        Assert.IsType<AcceptedResult>(result);
        Assert.Equal(BenchmarkRunStatus.Running, run.Status);
    }

    [Fact]
    public async Task ReassessAnswer_MarksRunRunning_BeforeReturningAccepted()
    {
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController(maxRunsPerHour: 10);
        var (suite, modelA, _, modelC) = await BenchmarkComplianceGuardTests.SeedConfigsAndSuite(db);

        var run = BuildSeedRun(suite, modelA, modelC);
        var answer = new BenchmarkRunAnswer
        {
            QuestionText = "Q1",
            AnswerText = "A1",
            Status = BenchmarkAnswerStatus.Ok,
            OrderIndex = 1
        };
        run.Answers.Add(answer);
        db.BenchmarkRuns.Add(run);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await controller.ReassessAnswer(run.Id, answer.Id, new ReassessAnswerRequest());

        Assert.IsType<AcceptedResult>(result);
        Assert.Equal(BenchmarkRunStatus.Running, run.Status);
        Assert.Null(run.CompletedAtUtc);
    }

    [Fact]
    public async Task RetryFailedClaimVerificationAsync_RestoresTerminalStatus_WhenNothingToRetry()
    {
        string dbName = Guid.NewGuid().ToString();
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        var config = BenchmarkComplianceGuardTests.CreateConfig(maxRunsPerHour: 10);
        var (service, runManager) = CreateTestBenchmarkService(dbOptions, config);

        using var seedDb = new ApplicationDbContext(dbOptions);
        var (suite, modelA, _, modelC) = await BenchmarkComplianceGuardTests.SeedConfigsAndSuite(seedDb);

        var run = BuildSeedRun(suite, modelA, modelC);
        run.Status = BenchmarkRunStatus.Running; // as the controller now leaves it before Task.Run
        run.Answers.Add(new BenchmarkRunAnswer
        {
            QuestionText = "Q1",
            AnswerText = "A1",
            Status = BenchmarkAnswerStatus.Ok,
            AssessmentStatus = BenchmarkAssessmentStatus.Scored,
            ClaimVerificationError = null,
            OrderIndex = 1
        });
        seedDb.BenchmarkRuns.Add(run);
        await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Mirrors what the controller would have done before firing the background task; the
        // service's own `finally` releases it, and that release is what this test checks.
        Assert.True(runManager.TryStart(run.Id, new CancellationTokenSource(), out _));

        await service.RetryFailedClaimVerificationAsync(run.Id, null, CancellationToken.None);

        using var freshDb = new ApplicationDbContext(dbOptions);
        var reloaded = await freshDb.BenchmarkRuns.FindAsync(new object[] { run.Id }, TestContext.Current.CancellationToken);
        Assert.NotNull(reloaded);
        Assert.NotEqual(BenchmarkRunStatus.Running, reloaded!.Status);
        Assert.Null(runManager.CurrentRunId);
    }

    [Fact]
    public async Task ReassessSingleQuestionAsync_RestoresCapturedStatus_WhenTrialCannotRun()
    {
        string dbName = Guid.NewGuid().ToString();
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        var config = BenchmarkComplianceGuardTests.CreateConfig(maxRunsPerHour: 10);
        var (service, runManager) = CreateTestBenchmarkService(dbOptions, config);

        using var seedDb = new ApplicationDbContext(dbOptions);
        var (suite, modelA, _, modelC) = await BenchmarkComplianceGuardTests.SeedConfigsAndSuite(seedDb);

        var run = BuildSeedRun(suite, modelA, modelC);
        run.Status = BenchmarkRunStatus.Running; // as the controller now leaves it before Task.Run
        run.CompletedAtUtc = null;
        var answer = new BenchmarkRunAnswer
        {
            QuestionText = "Q1",
            AnswerText = "A1",
            Status = BenchmarkAnswerStatus.Ok,
            OrderIndex = 1
        };
        run.Answers.Add(answer);
        seedDb.BenchmarkRuns.Add(run);
        await seedDb.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Mirrors what the controller would have done before firing the background task; the
        // service's `finally` releases what this TryStart claims, which is what the assertion
        // on runManager.CurrentRunId below checks.
        Assert.True(runManager.TryStart(run.Id, new CancellationTokenSource(), out _));

        var fixedCompletedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await service.ReassessSingleQuestionAsync(
            run.Id, answer.Id, null, trial: true,
            BenchmarkRunStatus.CompletedWithErrors, fixedCompletedAt, CancellationToken.None);

        using var freshDb = new ApplicationDbContext(dbOptions);
        var reloaded = await freshDb.BenchmarkRuns.FindAsync(new object[] { run.Id }, TestContext.Current.CancellationToken);
        Assert.NotNull(reloaded);
        Assert.Equal(BenchmarkRunStatus.CompletedWithErrors, reloaded!.Status);
        Assert.Equal(fixedCompletedAt, reloaded.CompletedAtUtc);
        Assert.Null(runManager.CurrentRunId);
    }
}
