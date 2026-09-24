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
/// Every operation that re-grades an existing run refuses the whole operation, before anything about
/// the run changes, when the rubric of an answer it would grade or the run's board was not recorded:
/// re-grading against today's suite would grade an old answer against a different answer key.
/// </summary>
public class BenchmarkRunExamRefusalTests
{
    private static DateTime FixedCompletedAt => new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

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

    public enum Unrecorded
    {
        Rubric,
        Board
    }

    public static TheoryData<Unrecorded> Cases => new() { Unrecorded.Rubric, Unrecorded.Board };

    /// <summary>
    /// A run on the current scoring method with one answer each operation acts on, whose rubric —
    /// or whose run's board — was not recorded.
    /// </summary>
    private static BenchmarkRun BuildRun(
        BenchmarkSuite suite, SystemAiApiConfiguration tested, SystemAiApiConfiguration assessor, Unrecorded what)
    {
        bool rubricRecorded = what != Unrecorded.Rubric;
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
            ScoringMethodVersion = BenchmarkAssessmentPrompt.ScoringMethodVersion,
            TotalQuestionCount = 2,
            StartedAtUtc = FixedCompletedAt.AddHours(-1),
            CompletedAtUtc = FixedCompletedAt,
            // A board hash with no board record: the board was replaced before runs recorded theirs.
            GameSnapshotSha256Used = what == Unrecorded.Board ? "board-sha" : null,
            GameSnapshotNameUsed = what == Unrecorded.Board ? "Board" : null
        });

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
            OrderIndex = 1,
            ExpectedPointsUsed = rubricRecorded ? "- point" : null,
            ExpectedPointsRecorded = rubricRecorded
        });
        run.Answers.Add(new BenchmarkRunAnswer
        {
            QuestionText = "Q2",
            AnswerText = string.Empty,
            Status = BenchmarkAnswerStatus.ProviderError,
            OrderIndex = 2,
            ExpectedPointsUsed = rubricRecorded ? "- point" : null,
            ExpectedPointsRecorded = rubricRecorded
        });
        return run;
    }

    private static void AssertUnchanged(BenchmarkRun run)
    {
        Assert.Equal(BenchmarkRunStatus.CompletedWithErrors, run.Status);
        Assert.Equal(FixedCompletedAt, run.CompletedAtUtc);
        Assert.Null(run.RerunStartedAtUtc);
        var first = run.Answers.First(a => a.OrderIndex == 1);
        Assert.Equal(4, first.AccuracyLevel);
        Assert.Equal(70, first.QualityScore);
        Assert.Equal(BenchmarkAssessmentStatus.Failed, first.AssessmentStatus);
        Assert.Equal("timeout", first.ClaimVerificationError);
    }

    private static string ExpectedRefusalStart(Unrecorded what) => what == Unrecorded.Rubric
        ? BenchmarkRunExamRecord.RubricNotRecordedRefusal
        : BenchmarkRunExamRecord.BoardNotRecordedRefusal;

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Service_RefusesEveryReGrade_WithoutTheRecord(Unrecorded what)
    {
        var ct = TestContext.Current.CancellationToken;
        var dbOptions = BenchmarkRunExamTests.InMemoryOptions();
        var (service, runManager) = CreateService(dbOptions);

        long runId;
        long answerId;
        await using (var seedDb = new ApplicationDbContext(dbOptions))
        {
            var (suite, modelA, _, modelC) = await BenchmarkComplianceGuardTests.SeedConfigsAndSuite(seedDb);
            var run = BuildRun(suite, modelA, modelC, what);
            // As the controller leaves the row before the background task starts.
            run.Status = BenchmarkRunStatus.Running;
            seedDb.BenchmarkRuns.Add(run);
            await seedDb.SaveChangesAsync(ct);
            runId = run.Id;
            answerId = run.Answers.First(a => a.OrderIndex == 1).Id;
        }

        var operations = new List<Func<Task>>
        {
            () => service.RerunSingleQuestionAsync(runId, answerId, null, CancellationToken.None),
            () => service.ReassessSingleQuestionAsync(
                runId, answerId, null, trial: false, BenchmarkRunStatus.CompletedWithErrors, FixedCompletedAt, CancellationToken.None),
            () => service.RetryFailedAssessmentsAsync(runId, null, CancellationToken.None),
            () => service.RetryFailedClaimVerificationAsync(runId, null, CancellationToken.None),
            () => service.RunFailedQuestionsAsync(runId, CancellationToken.None),
            () => service.RerunFinalSynthesisAsync(runId, null, CancellationToken.None)
        };

        foreach (var operation in operations)
        {
            Assert.True(runManager.TryStart(runId, new CancellationTokenSource(), out _));
            await operation();
            Assert.Null(runManager.CurrentRunId);

            await using var readback = new ApplicationDbContext(dbOptions);
            var reloaded = await readback.BenchmarkRuns.Include(r => r.Answers).FirstAsync(r => r.Id == runId, ct);
            AssertUnchanged(reloaded);
            Assert.StartsWith(ExpectedRefusalStart(what), reloaded.ErrorMessage);

            // Back to Running for the next operation, as its controller would leave it.
            reloaded.Status = BenchmarkRunStatus.Running;
            reloaded.ErrorMessage = null;
            await readback.SaveChangesAsync(ct);
        }

        // A calibration skips an answer without a recorded rubric, but refuses a run without its board.
        if (what == Unrecorded.Board)
        {
            await service.RunAssessorCalibrationAsync(runId, 3, "tester", CancellationToken.None);
            await using var readback = new ApplicationDbContext(dbOptions);
            Assert.Empty(readback.BenchmarkAssessorCalibrations);
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Service_TrialReassessment_WithoutTheRecord_RestoresTheCapturedStatus(Unrecorded what)
    {
        var ct = TestContext.Current.CancellationToken;
        var dbOptions = BenchmarkRunExamTests.InMemoryOptions();
        var (service, runManager) = CreateService(dbOptions);

        long runId;
        long answerId;
        await using (var seedDb = new ApplicationDbContext(dbOptions))
        {
            var (suite, modelA, _, modelC) = await BenchmarkComplianceGuardTests.SeedConfigsAndSuite(seedDb);
            var run = BuildRun(suite, modelA, modelC, what);
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
        AssertUnchanged(reloaded);
        Assert.Null(reloaded.Answers.First(a => a.Id == answerId).SecondOpinionQualityScore);
        Assert.Null(runManager.CurrentRunId);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Controller_RefusesEveryReGrade_WithoutTheRecord_As400(Unrecorded what)
    {
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController(maxRunsPerHour: 10);
        var (suite, modelA, _, modelC) = await BenchmarkComplianceGuardTests.SeedConfigsAndSuite(db);
        var run = BuildRun(suite, modelA, modelC, what);
        db.BenchmarkRuns.Add(run);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        long answerId = run.Answers.First(a => a.OrderIndex == 1).Id;

        var results = new List<IActionResult>
        {
            await controller.ReassessAnswer(run.Id, answerId, new ReassessAnswerRequest()),
            await controller.ReassessAnswer(run.Id, answerId, new ReassessAnswerRequest { Trial = true }),
            await controller.RerunAnswer(run.Id, answerId, new BenchmarkRetryRequest()),
            await controller.RerunSynthesis(run.Id, null),
            await controller.RetryFailedAssessments(run.Id, null),
            await controller.RetryClaimVerification(run.Id, null),
            await controller.RerunFailedQuestions(run.Id)
        };
        if (what == Unrecorded.Board)
        {
            results.Add(await controller.CalibrateAssessor(run.Id, new CalibrateAssessorRequest { AssessorModelConfigurationId = modelC.Id }));
        }

        foreach (var result in results)
        {
            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.StartsWith(ExpectedRefusalStart(what), Assert.IsType<string>(badRequest.Value));
        }

        AssertUnchanged(run);
        Assert.Empty(db.BenchmarkAssessorCalibrations);
    }

    [Fact]
    public async Task Controller_RefusalNamesTheUnrecordedQuestion()
    {
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController(maxRunsPerHour: 10);
        var (suite, modelA, _, modelC) = await BenchmarkComplianceGuardTests.SeedConfigsAndSuite(db);
        var run = BuildRun(suite, modelA, modelC, Unrecorded.Rubric);
        db.BenchmarkRuns.Add(run);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        long answerId = run.Answers.First(a => a.OrderIndex == 1).Id;

        var result = await controller.ReassessAnswer(run.Id, answerId, new ReassessAnswerRequest());

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.EndsWith("Questions: Q1.", Assert.IsType<string>(badRequest.Value));
    }

    [Fact]
    public async Task GetRunBoard_Is409ForAnUnknownBoard_404WithoutOne_AndTheRecordOtherwise()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, _) = BenchmarkComplianceGuardTests.CreateTestBenchmarkController(maxRunsPerHour: 10);
        var (suite, modelA, _, modelC) = await BenchmarkComplianceGuardTests.SeedConfigsAndSuite(db);

        var unknown = BuildRun(suite, modelA, modelC, Unrecorded.Board);
        var none = BuildRun(suite, modelA, modelC, Unrecorded.Rubric);
        var recorded = BuildRun(suite, modelA, modelC, Unrecorded.Board);
        recorded.BoardSnapshot = new BenchmarkRunBoardSnapshot
        {
            Sha256 = BenchmarkRunBoardSnapshotStore.ComputeSha256("HP: 12/60", "digest"),
            SanitizedText = "HP: 12/60",
            DigestText = "digest",
            CharCount = 9
        };
        db.BenchmarkRuns.AddRange(unknown, none, recorded);
        await db.SaveChangesAsync(ct);

        var conflict = Assert.IsType<ConflictObjectResult>(await controller.GetRunBoard(unknown.Id));
        Assert.Equal(BenchmarkRunExamRecord.BoardNotRecordedRefusal, conflict.Value);

        Assert.IsType<NotFoundResult>(await controller.GetRunBoard(none.Id));

        var ok = Assert.IsType<OkObjectResult>(await controller.GetRunBoard(recorded.Id));
        var board = Assert.IsType<BenchmarkRunBoardDto>(ok.Value);
        Assert.Equal("Board", board.Name);
        Assert.Equal("HP: 12/60", board.SanitizedText);
        Assert.Equal("digest", board.DigestText);
        Assert.Equal("board-sha", board.Sha256);
    }
}
