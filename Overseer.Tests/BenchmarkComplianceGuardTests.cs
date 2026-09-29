namespace Overseer.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
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
using Overseer.Services;
using Overseer.Services.Agents;
using Overseer.Services.Benchmarking;
using Overseer.Services.Privacy;
using Overseer.Services.Providers;
using Overseer.Tests.Helpers;
using Xunit;

public class BenchmarkComplianceGuardTests
{
    private static ApplicationDbContext CreateDbContext()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(dbOptions);
    }

    internal static IConfiguration CreateConfig(int maxQuestions = 50, int maxRunsPerDay = 20, int maxRunsPerHour = 5, string? purpose = null)
    {
        var dict = new Dictionary<string, string?>
        {
            { "Benchmark:Compliance:MaxQuestionsPerSuite", maxQuestions.ToString() },
            { "Benchmark:Compliance:MaxRunsPerDay", maxRunsPerDay.ToString() },
            { "Benchmark:Compliance:MaxRunsPerHour", maxRunsPerHour.ToString() }
        };

        if (purpose != null)
        {
            dict["Benchmark:Compliance:PurposeStatement"] = purpose;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(dict)
            .Build();
    }

    internal static BenchmarkRun CreateTestRun(string suiteName = "Test Suite", DateTime? startedAtUtc = null)
    {
        return new BenchmarkRun
        {
            SuiteName = suiteName,
            TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Google", modelId: "gemini-2.5-pro", displayName: "Gemini Pro"),
            AssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Anthropic", modelId: "claude-3-7-sonnet", displayName: "Claude Sonnet"),
            StartedAtUtc = startedAtUtc ?? DateTime.UtcNow
        };
    }

    // --- 1. Guard Unit Tests: Caps & Boundaries ---

    [Fact]
    public async Task CanSpendAsync_AllowsBelowHourlyCap_DeniesAtHourlyCap()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = CreateDbContext();
        var config = CreateConfig(maxRunsPerHour: 3, maxRunsPerDay: 20);
        var guard = new BenchmarkComplianceGuard(config, db);

        // Add 2 runs in trailing hour
        db.BenchmarkRuns.Add(CreateTestRun("Suite1", DateTime.UtcNow.AddMinutes(-40)));
        db.BenchmarkRuns.Add(CreateTestRun("Suite2", DateTime.UtcNow.AddMinutes(-20)));
        // Add 1 old run from 2 hours ago (should not count for hourly)
        db.BenchmarkRuns.Add(CreateTestRun("SuiteOld", DateTime.UtcNow.AddHours(-2)));
        await db.SaveChangesAsync(ct);

        var (allowed1, reason1) = await guard.CanSpendAsync(ct: ct);
        Assert.True(allowed1);
        Assert.Null(reason1);

        // Add 3rd run in trailing hour (hitting the cap of 3)
        db.BenchmarkRuns.Add(CreateTestRun("Suite3", DateTime.UtcNow.AddMinutes(-5)));
        await db.SaveChangesAsync(ct);

        var (allowed2, reason2) = await guard.CanSpendAsync(ct: ct);
        Assert.False(allowed2);
        Assert.NotNull(reason2);
        Assert.Contains("Hourly benchmark run cap reached (3 runs/hour)", reason2);
    }

    [Fact]
    public async Task CanSpendAsync_AllowsBelowDailyCap_DeniesAtDailyCap()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = CreateDbContext();
        var config = CreateConfig(maxRunsPerHour: 10, maxRunsPerDay: 4);
        var guard = new BenchmarkComplianceGuard(config, db);

        // Add 3 runs in past 24 hours (spaced out so hourly cap of 10 is not hit)
        db.BenchmarkRuns.Add(CreateTestRun("S1", DateTime.UtcNow.AddHours(-18)));
        db.BenchmarkRuns.Add(CreateTestRun("S2", DateTime.UtcNow.AddHours(-12)));
        db.BenchmarkRuns.Add(CreateTestRun("S3", DateTime.UtcNow.AddHours(-6)));
        // Add 1 old run from 30 hours ago (should not count for daily)
        db.BenchmarkRuns.Add(CreateTestRun("SOld", DateTime.UtcNow.AddHours(-30)));
        await db.SaveChangesAsync(ct);

        var (allowed1, reason1) = await guard.CanSpendAsync(ct: ct);
        Assert.True(allowed1);
        Assert.Null(reason1);

        // Add 4th run in past 24 hours (hitting the daily cap of 4)
        db.BenchmarkRuns.Add(CreateTestRun("S4", DateTime.UtcNow.AddHours(-2)));
        await db.SaveChangesAsync(ct);

        var (allowed2, reason2) = await guard.CanSpendAsync(ct: ct);
        Assert.False(allowed2);
        Assert.NotNull(reason2);
        Assert.Contains("Daily benchmark run cap reached (4 runs/day)", reason2);
    }

    [Fact]
    public async Task CanSpendAsync_CountsAcrossAllSuitesAndModels()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = CreateDbContext();
        var config = CreateConfig(maxRunsPerHour: 2, maxRunsPerDay: 20);
        var guard = new BenchmarkComplianceGuard(config, db);

        // 1 run on Suite 1 with Model A
        var r1 = CreateTestRun("Suite1", DateTime.UtcNow.AddMinutes(-30));
        r1.TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Google", modelId: "model-a", displayName: "Gemini Pro");
        db.BenchmarkRuns.Add(r1);

        // 1 run on Suite 2 with Model B
        var r2 = CreateTestRun("Suite2", DateTime.UtcNow.AddMinutes(-10));
        r2.TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Google", modelId: "model-b", displayName: "Gemini Pro");
        db.BenchmarkRuns.Add(r2);
        await db.SaveChangesAsync(ct);

        var (allowed, reason) = await guard.CanSpendAsync(ct: ct);
        Assert.False(allowed);
        Assert.Contains("Hourly benchmark run cap reached (2 runs/hour)", reason);
    }

    [Fact]
    public async Task CanAddQuestionsAsync_EnforcesSuiteCap()
    {
        var ct = TestContext.Current.CancellationToken;
        var db = CreateDbContext();
        var config = CreateConfig(maxQuestions: 3);
        var guard = new BenchmarkComplianceGuard(config, db);

        var suite = new BenchmarkSuite { Name = "Test Suite" };
        suite.Questions.Add(new BenchmarkQuestion { QuestionText = "Q1", OrderIndex = 1 });
        suite.Questions.Add(new BenchmarkQuestion { QuestionText = "Q2", OrderIndex = 2 });
        db.BenchmarkSuites.Add(suite);
        await db.SaveChangesAsync(ct);

        // Adding 1 question (2+1 = 3 <= 3) is allowed
        var (allowed1, reason1) = await guard.CanAddQuestionsAsync(suite.Id, 1, ct: ct);
        Assert.True(allowed1);
        Assert.Null(reason1);

        // Adding 2 questions (2+2 = 4 > 3) is denied
        var (allowed2, reason2) = await guard.CanAddQuestionsAsync(suite.Id, 2, ct: ct);
        Assert.False(allowed2);
        Assert.NotNull(reason2);
        Assert.Contains("Suite question limit reached (3 questions maximum)", reason2);
    }

    // --- 2. Guard Unit Tests: Same-Provider Detection ---

    [Theory]
    [InlineData("Google", "Google", true)]
    [InlineData("google", "GOOGLE", true)]
    [InlineData("Anthropic", "Anthropic", true)]
    [InlineData("Anthropic", "anthropic ", true)]
    [InlineData("OpenAI", "OpenAI", true)]
    [InlineData("Google", "Anthropic", false)]
    [InlineData("OpenAI", "Google", false)]
    [InlineData("Google", null, false)]
    [InlineData(null, "Google", false)]
    [InlineData("", "", false)]
    public void IsSameProvider_CorrectlyIdentifiesMatchingProviders(string? providerA, string? providerB, bool expectedSame)
    {
        var guard = new BenchmarkComplianceGuard(CreateConfig(), CreateDbContext());
        bool actual = guard.IsSameProvider(providerA, providerB);
        Assert.Equal(expectedSame, actual);
    }

    [Fact]
    public void IsSameProvider_ReturnsTrueForSameProviderWithDifferentModelIds()
    {
        var guard = new BenchmarkComplianceGuard(CreateConfig(), CreateDbContext());
        var configA = new SystemAiApiConfiguration { Provider = "Google", ModelId = "gemini-2.5-pro", DisplayName = "Gemini Pro" };
        var configB = new SystemAiApiConfiguration { Provider = "Google", ModelId = "gemini-1.5-flash", DisplayName = "Gemini Flash" };

        Assert.True(guard.IsSameProvider(configA, configB));
    }

    [Fact]
    public void GetPurposeStatement_ReturnsConfiguredOrDefault()
    {
        var defaultGuard = new BenchmarkComplianceGuard(CreateConfig(), CreateDbContext());
        Assert.Contains("Internal evaluation of candidate AI models", defaultGuard.GetPurposeStatement());

        var customGuard = new BenchmarkComplianceGuard(CreateConfig(purpose: "Custom purpose statement"), CreateDbContext());
        Assert.Equal("Custom purpose statement", customGuard.GetPurposeStatement());
    }

    // --- 3. Controller Endpoint Gating Tests ---

    internal static (AdminBenchmarkController controller, ApplicationDbContext db, BenchmarkComplianceGuard guard) CreateTestBenchmarkController(
        int maxRunsPerHour = 5, int maxRunsPerDay = 20, int maxQuestions = 50)
    {
        string dbName = Guid.NewGuid().ToString();
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        var db = new ApplicationDbContext(dbOptions);

        var config = CreateConfig(maxQuestions, maxRunsPerDay, maxRunsPerHour);
        var guard = new BenchmarkComplianceGuard(config, db);

        var runManager = new BenchmarkRunManager();

        var services = new ServiceCollection();
        services.AddScoped(_ => new ApplicationDbContext(dbOptions));
        services.AddScoped<SystemAiConfigService>();
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        var scoringProfileService = new BenchmarkScoringProfileService(scopeFactory, NullLogger<BenchmarkScoringProfileService>.Instance);

        var cryptoService = new CryptoService(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "AesEncryptionKey", Convert.ToBase64String(new byte[32]) }
        }).Build());

        var difficultyJobManager = new BenchmarkDifficultyJobManager();

        var endpointPolicy = new EndpointPolicy(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build());

        var benchmarkService = new BenchmarkService(
            scopeFactory,
            null!,
            null!,
            cryptoService,
            runManager,
            difficultyJobManager,
            scoringProfileService,
            endpointPolicy,
            config,
            NullLogger<BenchmarkService>.Instance);

        // StartRun delegates every validation to the launcher, so this one must be real: it is
        // the code path these tests are about. The orchestrator is only entered when RunCount > 1,
        // which these tests do not do, but it is cheap to build and a null would be a trap.
        var runLauncher = new BenchmarkRunLauncher(db, benchmarkService, runManager, guard, endpointPolicy);
        var seriesOrchestrator = new BenchmarkSeriesOrchestrator(
            scopeFactory, runManager, NullLogger<BenchmarkSeriesOrchestrator>.Instance);

        // The source and wiki indexes are only reached by the suite-health citation endpoint,
        // which these tests do not exercise — same reason the two nulls above are safe. The
        // snapshot importer is real: a suite import may attach a board.
        var controller = new AdminBenchmarkController(
            db, benchmarkService, scoringProfileService, runManager, difficultyJobManager, guard, scopeFactory,
            null!, null!, new BenchmarkSnapshotImporter(db), null!, null!, null!, null!, null!, null!,
            runLauncher, seriesOrchestrator, null!)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "test-user-id") }, "TestAuth"))
                }
            }
        };
        return (controller, db, guard);
    }

    internal static async Task<(BenchmarkSuite suite, SystemAiApiConfiguration modelA, SystemAiApiConfiguration modelB, SystemAiApiConfiguration modelC)> SeedConfigsAndSuite(ApplicationDbContext db)
    {
        var suite = new BenchmarkSuite { Name = "Test Suite", Description = "Desc" };
        suite.Questions.Add(new BenchmarkQuestion { QuestionText = "Q1", OrderIndex = 1, Difficulty = BenchmarkDifficulty.Simple, AssessedDifficulty = 25 });
        db.BenchmarkSuites.Add(suite);

        var modelA = new SystemAiApiConfiguration
        {
            DisplayName = "Gemini Pro",
            Provider = "Google",
            ModelId = "gemini-2.5-pro",
            EncryptedApiKey = "dummy_encrypted",
            ApiKeyNonce = "nonce",
            ApiKeyTag = "tag",
            ModelRole = 4, // Benchmark role
            IsEnabled = true
        };
        var modelB = new SystemAiApiConfiguration
        {
            DisplayName = "Gemini Flash",
            Provider = "Google",
            ModelId = "gemini-1.5-flash",
            EncryptedApiKey = "dummy_encrypted",
            ApiKeyNonce = "nonce",
            ApiKeyTag = "tag",
            ModelRole = 4,
            IsEnabled = true
        };
        var modelC = new SystemAiApiConfiguration
        {
            DisplayName = "Claude Sonnet",
            Provider = "Anthropic",
            ModelId = "claude-3-7-sonnet",
            EncryptedApiKey = "dummy_encrypted",
            ApiKeyNonce = "nonce",
            ApiKeyTag = "tag",
            ModelRole = 4,
            IsEnabled = true
        };

        db.SystemAiApiConfigurations.AddRange(modelA, modelB, modelC);
        await db.SaveChangesAsync();

        return (suite, modelA, modelB, modelC);
    }

    [Fact]
    public async Task StartRun_Returns429_WhenSpendCapExceeded()
    {
        var (controller, db, _) = CreateTestBenchmarkController(maxRunsPerHour: 1);
        var (suite, modelA, _, modelC) = await SeedConfigsAndSuite(db);

        // Insert 1 run in the trailing hour to exhaust the cap of 1
        db.BenchmarkRuns.Add(CreateTestRun("Prior", DateTime.UtcNow.AddMinutes(-10)));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var request = new StartBenchmarkRunRequest
        {
            SuiteId = suite.Id,
            TestedModelConfigurationId = modelA.Id,
            AssessorModelConfigurationId = modelC.Id
        };

        var result = await controller.StartRun(request);
        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status429TooManyRequests, objResult.StatusCode);
        Assert.Contains("Hourly benchmark run cap reached", objResult.Value?.ToString());
    }

    [Fact]
    public async Task StartRun_Returns409_WhenSameProviderAndNotAcknowledged()
    {
        var (controller, db, _) = CreateTestBenchmarkController(maxRunsPerHour: 10);
        var (suite, modelA, modelB, _) = await SeedConfigsAndSuite(db);

        // modelA (Google) tested with modelB (Google) without acknowledgement
        var request = new StartBenchmarkRunRequest
        {
            SuiteId = suite.Id,
            TestedModelConfigurationId = modelA.Id,
            AssessorModelConfigurationId = modelB.Id,
            AcknowledgeSameProvider = false
        };

        var result = await controller.StartRun(request);
        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, objResult.StatusCode);

        var warning = Assert.IsType<SameProviderWarningDto>(objResult.Value);
        Assert.True(warning.SameProvider);
        Assert.Equal("Google", warning.Provider);
        Assert.Equal("Gemini Pro", warning.TestedModelDisplayName);
        Assert.Equal("Gemini Flash", warning.AssessorModelDisplayName);
    }

    [Fact]
    public async Task StartRun_SucceedsAndPersistsComplianceFields_WhenAcknowledged()
    {
        var (controller, db, _) = CreateTestBenchmarkController(maxRunsPerHour: 10);
        var (suite, modelA, modelB, _) = await SeedConfigsAndSuite(db);

        var request = new StartBenchmarkRunRequest
        {
            SuiteId = suite.Id,
            TestedModelConfigurationId = modelA.Id,
            AssessorModelConfigurationId = modelB.Id,
            AcknowledgeSameProvider = true
        };

        var result = await controller.StartRun(request);
        var acceptedResult = Assert.IsType<AcceptedResult>(result);

        var run = await db.BenchmarkRuns.FirstAsync(TestContext.Current.CancellationToken);
        Assert.True(run.SameProviderAcknowledged);
        Assert.NotNull(run.PurposeStatementUsed);
        Assert.Contains("Internal evaluation of candidate AI models", run.PurposeStatementUsed);
    }

    [Fact]
    public async Task StartRun_SucceedsWithout409_ForCrossProvider()
    {
        var (controller, db, _) = CreateTestBenchmarkController(maxRunsPerHour: 10);
        var (suite, modelA, _, modelC) = await SeedConfigsAndSuite(db);

        // modelA (Google) tested with modelC (Anthropic)
        var request = new StartBenchmarkRunRequest
        {
            SuiteId = suite.Id,
            TestedModelConfigurationId = modelA.Id,
            AssessorModelConfigurationId = modelC.Id,
            AcknowledgeSameProvider = false
        };

        var result = await controller.StartRun(request);
        Assert.IsType<AcceptedResult>(result);

        var run = await db.BenchmarkRuns.FirstAsync(TestContext.Current.CancellationToken);
        Assert.False(run.SameProviderAcknowledged); // Cross provider does not require same-provider acknowledgement
        Assert.NotNull(run.PurposeStatementUsed);
    }

    [Fact]
    public async Task StartRun_AsksToAcknowledgeAReportWriterFromTheCandidatesProvider()
    {
        var (controller, db, _) = CreateTestBenchmarkController(maxRunsPerHour: 10);
        var (suite, modelA, modelB, modelC) = await SeedConfigsAndSuite(db);

        StartBenchmarkRunRequest Request(long writerId) => new()
        {
            SuiteId = suite.Id,
            TestedModelConfigurationId = modelA.Id,
            AssessorModelConfigurationId = modelC.Id,
            ReportWriterModelConfigurationId = writerId
        };

        // modelB is another Google model than the Google candidate: a warning to confirm, not a refusal.
        var sameProvider = Assert.IsType<ObjectResult>(await controller.StartRun(Request(modelB.Id)));
        Assert.Equal(StatusCodes.Status409Conflict, sameProvider.StatusCode);
        var warning = Assert.IsType<SameProviderWarningDto>(sameProvider.Value);
        Assert.Equal("reportWriter", warning.Role);
        Assert.True(warning.SameProvider);
        Assert.Equal("Google", warning.Provider);
        Assert.Equal("Gemini Pro", warning.TestedModelDisplayName);
        Assert.Equal("Gemini Flash", warning.AssessorModelDisplayName);
        Assert.Equal(
            "Gemini Flash is from Google, the provider of the model under test. Its reports may describe that model more favorably than an independent writer would.",
            warning.Message);

        // The model under test itself stays refused.
        var sameModel = Assert.IsType<BadRequestObjectResult>(await controller.StartRun(Request(modelA.Id)));
        Assert.Equal(BenchmarkRunReportDocumentService.ModelUnderTestMessage, sameModel.Value);

        var unknown = Assert.IsType<BadRequestObjectResult>(await controller.StartRun(Request(999999)));
        Assert.Equal(BenchmarkRunReportDocumentService.InvalidWriterMessage, unknown.Value);

        Assert.Empty(await db.BenchmarkRuns.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StartRun_StartsWithASameProviderReportWriterOnceAcknowledged()
    {
        var (controller, db, _) = CreateTestBenchmarkController(maxRunsPerHour: 10);
        var (suite, modelA, modelB, modelC) = await SeedConfigsAndSuite(db);

        var result = await controller.StartRun(new StartBenchmarkRunRequest
        {
            SuiteId = suite.Id,
            TestedModelConfigurationId = modelA.Id,
            AssessorModelConfigurationId = modelC.Id,
            ReportWriterModelConfigurationId = modelB.Id,
            AcknowledgeSameProviderReportWriter = true
        });
        Assert.IsType<AcceptedResult>(result);

        var run = await db.BenchmarkRuns.FirstAsync(TestContext.Current.CancellationToken);
        Assert.Equal(modelB.Id, run.ReportWriterModelConfigurationId);
        Assert.False(run.SameProviderAcknowledged); // The assessor is from another provider.
    }

    [Fact]
    public async Task StartRun_WithASameProviderAssessorAndWriter_AsksForTheAssessorFirst_ThenTheWriter_ThenStarts()
    {
        var (controller, db, _) = CreateTestBenchmarkController(maxRunsPerHour: 10);
        var (suite, modelA, modelB, _) = await SeedConfigsAndSuite(db);
        var writer = await AddConfigAsync(db, "Google", "gemini-2.0-flash-lite", "Gemini Flash Lite");

        StartBenchmarkRunRequest Request(bool assessorAck, bool writerAck) => new()
        {
            SuiteId = suite.Id,
            TestedModelConfigurationId = modelA.Id,
            AssessorModelConfigurationId = modelB.Id,
            ReportWriterModelConfigurationId = writer.Id,
            AcknowledgeSameProvider = assessorAck,
            AcknowledgeSameProviderReportWriter = writerAck
        };

        var first = Assert.IsType<ObjectResult>(await controller.StartRun(Request(assessorAck: false, writerAck: false)));
        Assert.Equal(StatusCodes.Status409Conflict, first.StatusCode);
        Assert.Equal("assessor", Assert.IsType<SameProviderWarningDto>(first.Value).Role);

        var second = Assert.IsType<ObjectResult>(await controller.StartRun(Request(assessorAck: true, writerAck: false)));
        Assert.Equal(StatusCodes.Status409Conflict, second.StatusCode);
        var writerWarning = Assert.IsType<SameProviderWarningDto>(second.Value);
        Assert.Equal("reportWriter", writerWarning.Role);
        Assert.Equal("Gemini Flash Lite", writerWarning.AssessorModelDisplayName);
        Assert.Empty(await db.BenchmarkRuns.ToListAsync(TestContext.Current.CancellationToken));

        Assert.IsType<AcceptedResult>(await controller.StartRun(Request(assessorAck: true, writerAck: true)));
        var run = await db.BenchmarkRuns.FirstAsync(TestContext.Current.CancellationToken);
        Assert.True(run.SameProviderAcknowledged);
        Assert.Equal(writer.Id, run.ReportWriterModelConfigurationId);
    }

    [Fact]
    public async Task StartRun_StampsAReportWriterFromAnotherProvider_OnTheRun()
    {
        var (controller, db, _) = CreateTestBenchmarkController(maxRunsPerHour: 10);
        var (suite, modelA, _, modelC) = await SeedConfigsAndSuite(db);

        var result = await controller.StartRun(new StartBenchmarkRunRequest
        {
            SuiteId = suite.Id,
            TestedModelConfigurationId = modelA.Id,
            AssessorModelConfigurationId = modelC.Id,
            ReportWriterModelConfigurationId = modelC.Id
        });
        Assert.IsType<AcceptedResult>(result);

        var run = await db.BenchmarkRuns.FirstAsync(TestContext.Current.CancellationToken);
        Assert.Equal(modelC.Id, run.ReportWriterModelConfigurationId);
        Assert.Equal(BenchmarkRunReportDocumentsStatus.NotRequested, run.ReportDocumentsStatus);
    }

    [Fact]
    public async Task SpendingEndpoints_Return429_WhenSpendCapExceeded()
    {
        var (controller, db, _) = CreateTestBenchmarkController(maxRunsPerHour: 1);
        var (suite, modelA, _, modelC) = await SeedConfigsAndSuite(db);

        // Create an existing run with answers
        var run = new BenchmarkRun
        {
            BenchmarkSuiteId = suite.Id,
            SuiteName = suite.Name,
            TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Google", modelId: "gemini", displayName: "Gemini"),
            AssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Anthropic", modelId: "claude", displayName: "Claude"),
            StartedAtUtc = DateTime.UtcNow.AddHours(-2)
        };
        var answer = new BenchmarkRunAnswer
        {
            QuestionText = "Q1",
            AnswerText = "Ans 1",
            Status = BenchmarkAnswerStatus.ProviderError,
            OrderIndex = 1
        };
        run.Answers.Add(answer);
        db.BenchmarkRuns.Add(run);

        // Add 1 recent run to exhaust hourly cap of 1
        db.BenchmarkRuns.Add(CreateTestRun("ExhaustingRun", DateTime.UtcNow.AddMinutes(-5)));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // 1. StartDifficultyAssessment -> 429
        var r1 = await controller.StartDifficultyAssessment(new StartDifficultyAssessmentRequest { SuiteId = suite.Id, AssessorModelConfigurationId = modelC.Id });
        Assert.Equal(StatusCodes.Status429TooManyRequests, Assert.IsType<ObjectResult>(r1).StatusCode);

        // 2. StartDifficultyAssessment for single question -> 429
        var qId = suite.Questions.First().Id;
        var r2 = await controller.StartDifficultyAssessment(new StartDifficultyAssessmentRequest { SuiteId = suite.Id, QuestionIds = new List<long> { qId }, AssessorModelConfigurationId = modelC.Id });
        Assert.Equal(StatusCodes.Status429TooManyRequests, Assert.IsType<ObjectResult>(r2).StatusCode);

        // 3. ReassessAnswer -> 429
        var r3 = await controller.ReassessAnswer(run.Id, answer.Id, new ReassessAnswerRequest { AssessorModelConfigurationId = modelC.Id });
        Assert.Equal(StatusCodes.Status429TooManyRequests, Assert.IsType<ObjectResult>(r3).StatusCode);

        // 4. RerunFailedQuestions -> 429
        var r4 = await controller.RerunFailedQuestions(run.Id);
        Assert.Equal(StatusCodes.Status429TooManyRequests, Assert.IsType<ObjectResult>(r4).StatusCode);
    }

    [Fact]
    public async Task RescoreRun_SucceedsEvenWhenSpendCapExceeded()
    {
        var (controller, db, _) = CreateTestBenchmarkController(maxRunsPerHour: 1);
        var (suite, _, _, _) = await SeedConfigsAndSuite(db);

        var run = new BenchmarkRun
        {
            BenchmarkSuiteId = suite.Id,
            SuiteName = suite.Name,
            TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Google", modelId: "gemini", displayName: "Gemini"),
            AssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Anthropic", modelId: "claude", displayName: "Claude"),
            StartedAtUtc = DateTime.UtcNow.AddHours(-2),
            Status = BenchmarkRunStatus.Completed
        };
        run.Answers.Add(new BenchmarkRunAnswer
        {
            QuestionText = "Q1",
            AnswerText = "Sample answer",
            Status = BenchmarkAnswerStatus.Ok,
            AccuracyLevel = 5,
            CompletenessLevel = 5,
            ConcisenessLevel = 5,
            ReadabilityLevel = 5,
            OrderIndex = 1
        });
        db.BenchmarkRuns.Add(run);

        // Add 1 recent run to exhaust hourly cap
        db.BenchmarkRuns.Add(CreateTestRun("CapExhausted", DateTime.UtcNow.AddMinutes(-5)));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Rescore is pure arithmetic and must not be gated
        var result = await controller.RescoreRun(run.Id, new RescoreRunRequest());
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task CreateQuestion_And_DuplicateSuite_EnforceSuiteCap()
    {
        var (controller, db, _) = CreateTestBenchmarkController(maxQuestions: 2);
        var (suite, _, _, _) = await SeedConfigsAndSuite(db); // suite has 1 question

        // Add 1 question -> suite now has 2 questions (at cap)
        var addRes1 = await controller.CreateQuestion(suite.Id, new CreateBenchmarkQuestionRequest
        {
            QuestionText = "Q2",
            Difficulty = BenchmarkDifficulty.Simple
        });
        Assert.IsType<OkObjectResult>(addRes1);

        // Add 2nd question -> exceeds cap of 2 -> 400 Bad Request
        var addRes2 = await controller.CreateQuestion(suite.Id, new CreateBenchmarkQuestionRequest
        {
            QuestionText = "Q3",
            Difficulty = BenchmarkDifficulty.Simple
        });
        var badReq = Assert.IsType<BadRequestObjectResult>(addRes2);
        Assert.Contains("Suite question limit reached (2 questions maximum)", badReq.Value?.ToString());
    }

    [Fact]
    public async Task StoredFootprint_And_BulkDeleteSuiteRuns_WorkCorrectly()
    {
        var (controller, db, _) = CreateTestBenchmarkController();
        var (suite, _, _, _) = await SeedConfigsAndSuite(db);

        var run1 = new BenchmarkRun
        {
            BenchmarkSuiteId = suite.Id,
            SuiteName = suite.Name,
            TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Google", modelId: "gemini", displayName: "Gemini"),
            AssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Anthropic", modelId: "claude", displayName: "Claude")
        };
        run1.Answers.Add(new BenchmarkRunAnswer { QuestionText = "Q1", AnswerText = "12345", OrderIndex = 1 });
        run1.Answers.Add(new BenchmarkRunAnswer { QuestionText = "Q2", AnswerText = "67890", OrderIndex = 2 });

        var run2 = new BenchmarkRun
        {
            BenchmarkSuiteId = suite.Id,
            SuiteName = suite.Name,
            TestedModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Google", modelId: "gemini", displayName: "Gemini"),
            AssessorModelSnapshot = BenchmarkModelSnapshots.Model(provider: "Anthropic", modelId: "claude", displayName: "Claude")
        };
        run2.Answers.Add(new BenchmarkRunAnswer { QuestionText = "Q1", AnswerText = "abc", OrderIndex = 1 });

        db.BenchmarkRuns.AddRange(run1, run2);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        // 1. Get Footprint
        var fpResult = await controller.GetSuiteRunsFootprint(suite.Id);
        var okFp = Assert.IsType<OkObjectResult>(fpResult);
        var fp = Assert.IsType<BenchmarkFootprintDto>(okFp.Value);
        Assert.Equal(2, fp.RunCount);
        Assert.Equal(13, fp.TotalAnswerCharacters); // 5 + 5 + 3 = 13 chars

        // 2. Delete Suite Runs
        var delResult = await controller.DeleteSuiteRuns(suite.Id);
        Assert.IsType<OkObjectResult>(delResult);

        // 3. Footprint is now 0
        var fpResultAfter = await controller.GetSuiteRunsFootprint(suite.Id);
        var fpAfter = Assert.IsType<BenchmarkFootprintDto>(Assert.IsType<OkObjectResult>(fpResultAfter).Value);
        Assert.Equal(0, fpAfter.RunCount);
        Assert.Equal(0, fpAfter.TotalAnswerCharacters);
    }

    [Fact]
    public async Task StartRun_RejectsUnassessedSuite_AndAllowsFullyAssessedSuite()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, _) = CreateTestBenchmarkController(maxRunsPerHour: 10);
        var (suite, modelA, _, modelC) = await SeedConfigsAndSuite(db);

        // Add an unassessed question to the suite
        suite.Questions.Add(new BenchmarkQuestion
        {
            QuestionText = "Q2 Unassessed",
            OrderIndex = 2,
            Difficulty = BenchmarkDifficulty.Intermediate,
            AssessedDifficulty = null
        });
        await db.SaveChangesAsync(ct);

        var request = new StartBenchmarkRunRequest
        {
            SuiteId = suite.Id,
            TestedModelConfigurationId = modelA.Id,
            AssessorModelConfigurationId = modelC.Id,
            AcknowledgeSameProvider = false
        };

        // Should be rejected because 1 question is unassessed
        var rejectedResult = await controller.StartRun(request);
        var badRequest = Assert.IsType<BadRequestObjectResult>(rejectedResult);
        var errorMsg = Assert.IsType<string>(badRequest.Value);
        Assert.Contains("without an assessed difficulty", errorMsg);
        Assert.Contains("1 of 2 question(s)", errorMsg);

        // Now assess the question
        suite.Questions.First(q => q.OrderIndex == 2).AssessedDifficulty = 60;
        await db.SaveChangesAsync(ct);

        // Should succeed
        var acceptedResult = await controller.StartRun(request);
        Assert.IsType<AcceptedResult>(acceptedResult);
    }

    // --- 4. Panel Launch Rules ---

    private static SystemAiApiConfiguration BenchmarkConfig(string provider, string modelId, string displayName) => new()
    {
        DisplayName = displayName,
        Provider = provider,
        ModelId = modelId,
        EncryptedApiKey = "dummy_encrypted",
        ApiKeyNonce = "nonce",
        ApiKeyTag = "tag",
        ModelRole = 4,
        IsEnabled = true
    };

    private static async Task<SystemAiApiConfiguration> AddConfigAsync(ApplicationDbContext db, string provider, string modelId, string displayName)
    {
        var config = BenchmarkConfig(provider, modelId, displayName);
        db.SystemAiApiConfigurations.Add(config);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return config;
    }

    [Fact]
    public async Task StartRun_Panel_WithBothMembersFromOneProvider_Returns400_AndCreatesNoRun()
    {
        var (controller, db, _) = CreateTestBenchmarkController(maxRunsPerHour: 10);
        var (suite, modelA, _, modelC) = await SeedConfigsAndSuite(db);
        var otherAnthropic = await AddConfigAsync(db, "Anthropic", "claude-opus-4-6", "Claude Opus");

        var result = await controller.StartRun(new StartBenchmarkRunRequest
        {
            SuiteId = suite.Id,
            TestedModelConfigurationId = modelA.Id,
            AssessorModelConfigurationId = modelC.Id,
            CoAssessorModelConfigurationId = otherAnthropic.Id
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("A panel needs members from two providers", Assert.IsType<string>(badRequest.Value));
        Assert.Empty(db.BenchmarkRuns);
    }

    [Fact]
    public async Task StartRun_Panel_WhenTheCandidateModelIsTheCoAssessorModel_Returns400()
    {
        var (controller, db, _) = CreateTestBenchmarkController(maxRunsPerHour: 10);
        var (suite, _, _, modelC) = await SeedConfigsAndSuite(db);
        var candidate = await AddConfigAsync(db, "OpenAI", "gpt-5.5", "GPT 5.5");
        // Another configuration row of the same model: the rule is on the model, not the row.
        var member = await AddConfigAsync(db, "OpenAI", " GPT-5.5 ", "GPT 5.5 (grader)");

        var result = await controller.StartRun(new StartBenchmarkRunRequest
        {
            SuiteId = suite.Id,
            TestedModelConfigurationId = candidate.Id,
            AssessorModelConfigurationId = modelC.Id,
            CoAssessorModelConfigurationId = member.Id
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("cannot grade itself", Assert.IsType<string>(badRequest.Value));
        Assert.Empty(db.BenchmarkRuns);
    }

    [Fact]
    public async Task StartRun_Panel_WhenTheCandidateModelIsTheAssessorModel_Returns400()
    {
        var (controller, db, _) = CreateTestBenchmarkController(maxRunsPerHour: 10);
        var (suite, _, _, modelC) = await SeedConfigsAndSuite(db);
        var openAiMember = await AddConfigAsync(db, "OpenAI", "gpt-4.1", "GPT 4.1");

        // A single-assessor run would only ask for acknowledgement here; a panel run refuses.
        var result = await controller.StartRun(new StartBenchmarkRunRequest
        {
            SuiteId = suite.Id,
            TestedModelConfigurationId = modelC.Id,
            AssessorModelConfigurationId = modelC.Id,
            CoAssessorModelConfigurationId = openAiMember.Id,
            AcknowledgeSameProvider = true
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("cannot grade itself", Assert.IsType<string>(badRequest.Value));
        Assert.Empty(db.BenchmarkRuns);
    }

    [Fact]
    public async Task StartRun_Panel_OpenAiCandidateWithAnOlderOpenAiMember_IsAcceptedWithout409()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, _) = CreateTestBenchmarkController(maxRunsPerHour: 10);
        var (suite, _, _, modelC) = await SeedConfigsAndSuite(db);
        var candidate = await AddConfigAsync(db, "OpenAI", "gpt-5.5", "GPT 5.5");
        var openAiMember = await AddConfigAsync(db, "OpenAI", "gpt-4.1", "GPT 4.1");

        // Same provider as the candidate, not acknowledged: the panel's balance answers same-family
        // preference, so the acknowledgeable gate does not apply.
        var result = await controller.StartRun(new StartBenchmarkRunRequest
        {
            SuiteId = suite.Id,
            TestedModelConfigurationId = candidate.Id,
            AssessorModelConfigurationId = openAiMember.Id,
            CoAssessorModelConfigurationId = modelC.Id,
            AcknowledgeSameProvider = false
        });

        Assert.IsType<AcceptedResult>(result);
        var run = await db.BenchmarkRuns.FirstAsync(ct);
        Assert.Equal(openAiMember.Id, run.AssessorModelConfigurationId);
        Assert.Equal(modelC.Id, run.CoAssessorModelConfigurationId);
        Assert.NotNull(run.CoAssessorModelSnapshot);
        Assert.Equal("Anthropic", run.CoAssessorModelSnapshot!.Provider);
        Assert.True(BenchmarkRunFinalizer.IsPanelRun(run));
        Assert.False(run.SameProviderAcknowledged);
    }

    [Fact]
    public async Task StartRun_Panel_ForcesTheReferenceReaderToAll_WhateverModeWasRequested()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, _) = CreateTestBenchmarkController(maxRunsPerHour: 10);
        var (suite, _, modelB, modelC) = await SeedConfigsAndSuite(db);
        var candidate = await AddConfigAsync(db, "OpenAI", "gpt-5.5", "GPT 5.5");
        var openAiMember = await AddConfigAsync(db, "OpenAI", "gpt-4.1", "GPT 4.1");

        var result = await controller.StartRun(new StartBenchmarkRunRequest
        {
            SuiteId = suite.Id,
            TestedModelConfigurationId = candidate.Id,
            AssessorModelConfigurationId = openAiMember.Id,
            CoAssessorModelConfigurationId = modelC.Id,
            SecondOpinionAssessorModelConfigurationId = modelB.Id,
            SecondOpinionMode = (int)BenchmarkSecondOpinionMode.Flagged
        });

        Assert.IsType<AcceptedResult>(result);
        var run = await db.BenchmarkRuns.FirstAsync(ct);
        Assert.Equal(modelB.Id, run.SecondOpinionAssessorModelConfigurationId);
        Assert.Equal((int)BenchmarkSecondOpinionMode.All, run.SecondOpinionModeUsed);
    }

    [Fact]
    public async Task ReassessAnswer_TrialOverTheReferenceReadersVerdict_OnAPanelRun_Is409_EvenWithReplaceRequested()
    {
        var ct = TestContext.Current.CancellationToken;
        var (controller, db, _) = CreateTestBenchmarkController(maxRunsPerHour: 10);
        var (suite, modelA, modelB, modelC) = await SeedConfigsAndSuite(db);
        var openAiMember = await AddConfigAsync(db, "OpenAI", "gpt-4.1", "GPT 4.1");
        var completedAt = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

        var run = BenchmarkModelSnapshots.Attach(new BenchmarkRun
        {
            Status = BenchmarkRunStatus.Completed,
            BenchmarkSuiteId = suite.Id,
            SuiteName = suite.Name,
            TestedModelConfigurationId = modelA.Id,
            AssessorModelConfigurationId = modelC.Id,
            CoAssessorModelConfigurationId = openAiMember.Id,
            SecondOpinionAssessorModelConfigurationId = modelB.Id,
            SecondOpinionModeUsed = (int)BenchmarkSecondOpinionMode.All,
            CandidatePromptOptionsJson = "{}",
            ScoringMethodVersion = BenchmarkAssessmentPrompt.ScoringMethodVersion,
            TotalQuestionCount = 1,
            StartedAtUtc = completedAt.AddHours(-1),
            CompletedAtUtc = completedAt
        });
        var answer = new BenchmarkRunAnswer
        {
            QuestionText = "Q1",
            AnswerText = "A1",
            Status = BenchmarkAnswerStatus.Ok,
            OrderIndex = 1,
            ExpectedPointsUsed = "- point",
            ExpectedPointsRecorded = true,
            AssessmentStatus = BenchmarkAssessmentStatus.Scored,
            QualityScore = 80,
            CoAssessmentStatus = BenchmarkAssessmentStatus.Scored,
            CoAssessmentQualityScore = 70,
            PanelQualityScore = 75,
            SecondOpinionQualityScore = 60,
            SecondOpinionCriticalError = false
        };
        run.Answers.Add(answer);
        db.BenchmarkRuns.Add(run);
        await db.SaveChangesAsync(ct);

        var result = await controller.ReassessAnswer(run.Id, answer.Id, new ReassessAnswerRequest
        {
            Trial = true,
            ReplaceExistingSecondOpinion = true,
            AssessorModelConfigurationId = modelB.Id
        });

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(BenchmarkService.PanelTrialReplaceRefusedMessage, conflict.Value);
        Assert.Equal(BenchmarkRunStatus.Completed, run.Status);
        Assert.Equal(completedAt, run.CompletedAtUtc);
        Assert.Equal(60, answer.SecondOpinionQualityScore);
    }
}
