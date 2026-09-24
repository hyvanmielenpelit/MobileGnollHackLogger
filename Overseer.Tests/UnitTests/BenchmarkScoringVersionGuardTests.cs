namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
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
using Overseer.Tests.Helpers;
using Xunit;

/// <summary>
/// Every operation that grades part of a run is refused on a run graded under another scoring
/// method, before anything about the run changes. Reports, Rescore and the final-synthesis re-run
/// stay available, and Rescore never stamps a method onto levels it did not grade.
/// </summary>
public class BenchmarkScoringVersionGuardTests
{
    // 0 is what a row that was never stamped holds; 10 is the method before the anchor change.
    public static TheoryData<int> ForeignMethods => new() { 0, 10 };

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

    private static DateTime FixedCompletedAt => new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    private static BenchmarkRun BuildRun(
        BenchmarkSuite suite, SystemAiApiConfiguration tested, SystemAiApiConfiguration assessor, int scoringMethod)
    {
        var run = BenchmarkModelSnapshots.Attach(new BenchmarkRun
        {
            Status = BenchmarkRunStatus.CompletedWithErrors,
            BenchmarkSuiteId = suite.Id,
            SuiteName = suite.Name,
            TestedModelConfigurationId = tested.Id,
            TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: tested.Provider!, modelId: tested.ModelId!, displayName: tested.DisplayName!),
            AssessorModelConfigurationId = assessor.Id,
            AssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: assessor.Provider!, modelId: assessor.ModelId!, displayName: assessor.DisplayName!),
            ClaimVerifierModelConfigurationId = assessor.Id,
            CandidatePromptOptionsJson = "{}",
            ScoringMethodVersion = scoringMethod,
            TotalQuestionCount = 2,
            StartedAtUtc = FixedCompletedAt.AddHours(-1),
            CompletedAtUtc = FixedCompletedAt
        });

        // One answer each operation below would act on: a provider error (re-run failed), an
        // unscored assessment (retry assessments) and a failed verification (retry verification).
        run.Answers.Add(new BenchmarkRunAnswer
        {
            QuestionText = "Q1",
            AnswerText = "A1",
            Status = BenchmarkAnswerStatus.Ok,
            AssessmentStatus = BenchmarkAssessmentStatus.Failed,
            AccuracyLevel = 4,
            CompletenessLevel = 4,
            ConcisenessLevel = 4,
            ReadabilityLevel = 4,
            QualityScore = 70,
            ClaimVerificationError = "timeout",
            OrderIndex = 1
        });
        run.Answers.Add(new BenchmarkRunAnswer
        {
            QuestionText = "Q2",
            AnswerText = string.Empty,
            Status = BenchmarkAnswerStatus.ProviderError,
            OrderIndex = 2
        });
        return run;
    }

    private static void AssertUnchanged(BenchmarkRun run, BenchmarkRunStatus status)
    {
        Assert.Equal(status, run.Status);
        Assert.Equal(FixedCompletedAt, run.CompletedAtUtc);
        Assert.Null(run.RerunStartedAtUtc);
        var first = run.Answers.First(a => a.OrderIndex == 1);
        Assert.Equal(4, first.AccuracyLevel);
        Assert.Equal(70, first.QualityScore);
        Assert.Equal(BenchmarkAssessmentStatus.Failed, first.AssessmentStatus);
        Assert.Equal("timeout", first.ClaimVerificationError);
    }

    [Theory]
    [MemberData(nameof(ForeignMethods))]
    public async Task Controller_RefusesEveryGradingOperation_OnAnotherMethod(int method)
    {
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController(maxRunsPerHour: 10);
        var (suite, modelA, _, modelC) = await BenchmarkComplianceGuardTests.SeedConfigsAndSuite(db);
        var run = BuildRun(suite, modelA, modelC, method);
        db.BenchmarkRuns.Add(run);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        long answerId = run.Answers.First(a => a.OrderIndex == 1).Id;
        string refusal = BenchmarkService.ScoringMethodRefusal(run);

        var results = new List<IActionResult>
        {
            await controller.ReassessAnswer(run.Id, answerId, new ReassessAnswerRequest()),
            await controller.ReassessAnswer(run.Id, answerId, new ReassessAnswerRequest { Trial = true }),
            await controller.CalibrateAssessor(run.Id, new CalibrateAssessorRequest { AssessorModelConfigurationId = modelC.Id }),
            await controller.RerunAnswer(run.Id, answerId, new BenchmarkRetryRequest()),
            await controller.RetryFailedAssessments(run.Id, null),
            await controller.RetryClaimVerification(run.Id, null),
            await controller.RerunFailedQuestions(run.Id)
        };

        foreach (var result in results)
        {
            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(refusal, badRequest.Value);
        }

        AssertUnchanged(run, BenchmarkRunStatus.CompletedWithErrors);
        Assert.Empty(db.BenchmarkAssessorCalibrations);
    }

    [Fact]
    public void Refusal_NamesTheRunAndBothMethods()
    {
        var run = BenchmarkModelSnapshots.Attach(new BenchmarkRun { Id = 53, ScoringMethodVersion = 10 });

        Assert.False(BenchmarkService.IsCurrentScoringMethod(run));
        Assert.Equal(
            $"Refused: run 53 was graded under scoring method 10, and this build grades under {BenchmarkAssessmentPrompt.ScoringMethodVersion}. "
            + "Mixing the two inside one run would make its scores meaningless. Start a new run instead.",
            BenchmarkService.ScoringMethodRefusal(run));

        run.ScoringMethodVersion = BenchmarkAssessmentPrompt.ScoringMethodVersion;
        Assert.True(BenchmarkService.IsCurrentScoringMethod(run));
    }

    [Fact]
    public async Task Controller_AcceptsGradingOperations_OnTheCurrentMethod()
    {
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController(maxRunsPerHour: 10);
        var (suite, modelA, _, modelC) = await BenchmarkComplianceGuardTests.SeedConfigsAndSuite(db);
        var run = BuildRun(suite, modelA, modelC, BenchmarkAssessmentPrompt.ScoringMethodVersion);
        db.BenchmarkRuns.Add(run);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Calibrate is the one grading endpoint BenchmarkRerunStatusTests does not already accept.
        var result = await controller.CalibrateAssessor(run.Id, new CalibrateAssessorRequest { AssessorModelConfigurationId = modelC.Id });

        Assert.IsType<AcceptedResult>(result);
    }

    [Fact]
    public async Task Controller_RefusesCalibration_OfAnAbortedRun()
    {
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController(maxRunsPerHour: 10);
        var (suite, modelA, _, modelC) = await BenchmarkComplianceGuardTests.SeedConfigsAndSuite(db);
        var run = BuildRun(suite, modelA, modelC, BenchmarkAssessmentPrompt.ScoringMethodVersion);
        run.Status = BenchmarkRunStatus.Canceled;
        run.TotalQuestionCount = 5;
        db.BenchmarkRuns.Add(run);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await controller.CalibrateAssessor(run.Id, new CalibrateAssessorRequest { AssessorModelConfigurationId = modelC.Id });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(BenchmarkService.AbortedRunRefusal, badRequest.Value);
    }

    [Fact]
    public async Task Controller_AcceptsFinalSynthesisRerun_OnAnOlderMethod()
    {
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController(maxRunsPerHour: 10);
        var (suite, modelA, _, modelC) = await BenchmarkComplianceGuardTests.SeedConfigsAndSuite(db);
        var run = BuildRun(suite, modelA, modelC, 10);
        db.BenchmarkRuns.Add(run);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await controller.RerunSynthesis(run.Id, null);

        Assert.IsType<AcceptedResult>(result);
    }

    [Theory]
    [MemberData(nameof(ForeignMethods))]
    public async Task Service_RefusesEveryGradingOperation_OnAnotherMethod(int method)
    {
        var ct = TestContext.Current.CancellationToken;
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var (service, runManager) = CreateService(dbOptions);

        long runId;
        long answerId;
        await using (var seedDb = new ApplicationDbContext(dbOptions))
        {
            var (suite, modelA, _, modelC) = await BenchmarkComplianceGuardTests.SeedConfigsAndSuite(seedDb);
            var run = BuildRun(suite, modelA, modelC, method);
            // As the controller leaves the row before the background task starts.
            run.Status = BenchmarkRunStatus.Running;
            seedDb.BenchmarkRuns.Add(run);
            await seedDb.SaveChangesAsync(ct);
            runId = run.Id;
            answerId = run.Answers.First(a => a.OrderIndex == 1).Id;
        }

        var operations = new List<Func<Task>>
        {
            () => service.ReassessSingleQuestionAsync(
                runId, answerId, null, trial: false, BenchmarkRunStatus.CompletedWithErrors, FixedCompletedAt, CancellationToken.None),
            () => service.RerunSingleQuestionAsync(runId, answerId, null, CancellationToken.None),
            () => service.RetryFailedAssessmentsAsync(runId, null, CancellationToken.None),
            () => service.RetryFailedClaimVerificationAsync(runId, null, CancellationToken.None),
            () => service.RunFailedQuestionsAsync(runId, CancellationToken.None)
        };

        foreach (var operation in operations)
        {
            Assert.True(runManager.TryStart(runId, new CancellationTokenSource(), out _));
            await operation();
            Assert.Null(runManager.CurrentRunId);

            await using var readback = new ApplicationDbContext(dbOptions);
            var reloaded = await readback.BenchmarkRuns.Include(r => r.Answers).FirstAsync(r => r.Id == runId, ct);
            AssertUnchanged(reloaded, BenchmarkRunStatus.CompletedWithErrors);
            Assert.Equal(BenchmarkService.ScoringMethodRefusal(reloaded), reloaded.ErrorMessage);
            Assert.Equal(method, reloaded.ScoringMethodVersion);

            // Back to Running for the next operation, as its controller would leave it.
            reloaded.Status = BenchmarkRunStatus.Running;
            reloaded.ErrorMessage = null;
            await readback.SaveChangesAsync(ct);
        }

        await service.RunAssessorCalibrationAsync(runId, 3, "tester", CancellationToken.None);
        await using (var readback = new ApplicationDbContext(dbOptions))
        {
            Assert.Empty(readback.BenchmarkAssessorCalibrations);
        }
    }

    [Fact]
    public async Task Service_TrialReassessment_OnAnotherMethod_RestoresTheCapturedStatus()
    {
        var ct = TestContext.Current.CancellationToken;
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var (service, runManager) = CreateService(dbOptions);

        long runId;
        long answerId;
        await using (var seedDb = new ApplicationDbContext(dbOptions))
        {
            var (suite, modelA, _, modelC) = await BenchmarkComplianceGuardTests.SeedConfigsAndSuite(seedDb);
            var run = BuildRun(suite, modelA, modelC, 10);
            run.Status = BenchmarkRunStatus.Running;
            run.CompletedAtUtc = null;
            seedDb.BenchmarkRuns.Add(run);
            await seedDb.SaveChangesAsync(ct);
            runId = run.Id;
            answerId = run.Answers.First(a => a.OrderIndex == 1).Id;
        }

        Assert.True(runManager.TryStart(runId, new CancellationTokenSource(), out _));
        await service.ReassessSingleQuestionAsync(
            runId, answerId, null, trial: true, BenchmarkRunStatus.CompletedWithErrors, FixedCompletedAt, CancellationToken.None);

        await using var readback = new ApplicationDbContext(dbOptions);
        var reloaded = await readback.BenchmarkRuns.Include(r => r.Answers).FirstAsync(r => r.Id == runId, ct);
        AssertUnchanged(reloaded, BenchmarkRunStatus.CompletedWithErrors);
        Assert.Null(reloaded.Answers.First(a => a.Id == answerId).SecondOpinionQualityScore);
        Assert.Null(runManager.CurrentRunId);
    }

    [Theory]
    [InlineData(0, BenchmarkService.LastMethodRescoreCanApply)]
    [InlineData(3, BenchmarkService.LastMethodRescoreCanApply)]
    [InlineData(10, 10)]
    [InlineData(11, 11)]
    public async Task Rescore_RecomputesTheIndices_AndNeverStampsAMethodItDidNotGrade(int storedMethod, int expectedMethod)
    {
        var ct = TestContext.Current.CancellationToken;
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var (service, _) = CreateService(dbOptions);

        await using (var seedDb = new ApplicationDbContext(dbOptions))
        {
            var run = new BenchmarkRun
            {
                Id = 53,
                SuiteName = "Suite",
                TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Provider", modelId: "candidate", displayName: "Candidate"),
                AssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Provider", modelId: "assessor", displayName: "Assessor"),
                Status = BenchmarkRunStatus.Completed,
                StartedAtUtc = FixedCompletedAt.AddHours(-1),
                CompletedAtUtc = FixedCompletedAt,
                TotalQuestionCount = 1,
                ScoringMethodVersion = storedMethod
            };
            run.Answers.Add(new BenchmarkRunAnswer
            {
                OrderIndex = 1,
                QuestionText = "Q1",
                AnswerText = "A1",
                Status = BenchmarkAnswerStatus.Ok,
                AssessmentStatus = BenchmarkAssessmentStatus.Scored,
                AccuracyLevel = 5,
                CompletenessLevel = 5,
                ConcisenessLevel = 5,
                ReadabilityLevel = 5,
                AssessedDifficulty = 50,
                DurationMs = 2000
            });
            seedDb.BenchmarkRuns.Add(run);
            await seedDb.SaveChangesAsync(ct);
        }

        var (success, error) = await service.RescoreRunAsync(53);
        Assert.True(success, error);

        await using var readback = new ApplicationDbContext(dbOptions);
        var rescored = await readback.BenchmarkRuns.Include(r => r.Answers).FirstAsync(r => r.Id == 53, ct);
        Assert.Equal(expectedMethod, rescored.ScoringMethodVersion);
        Assert.NotNull(rescored.Answers.Single().QualityScore);
        Assert.NotNull(rescored.QualityIndex);
    }
}
