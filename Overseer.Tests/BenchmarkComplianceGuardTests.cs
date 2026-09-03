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
using Overseer.Services.Providers;
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

    private static IConfiguration CreateConfig(int maxQuestions = 50, int maxRunsPerDay = 20, int maxRunsPerHour = 5, string? purpose = null)
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

    private static BenchmarkRun CreateTestRun(string suiteName = "Test Suite", DateTime? startedAtUtc = null)
    {
        return new BenchmarkRun
        {
            SuiteName = suiteName,
            TestedModelProviderUsed = "Google",
            TestedModelDisplayNameUsed = "Gemini Pro",
            TestedModelIdUsed = "gemini-2.5-pro",
            AssessorModelProviderUsed = "Anthropic",
            AssessorModelDisplayNameUsed = "Claude Sonnet",
            AssessorModelIdUsed = "claude-3-7-sonnet",
            StartedAtUtc = startedAtUtc ?? DateTime.UtcNow
        };
    }

    // --- 1. Guard Unit Tests: Caps & Boundaries ---

    [Fact]
    public async Task CanSpendAsync_AllowsBelowHourlyCap_DeniesAtHourlyCap()
    {
        var db = CreateDbContext();
        var config = CreateConfig(maxRunsPerHour: 3, maxRunsPerDay: 20);
        var guard = new BenchmarkComplianceGuard(config, db);

        // Add 2 runs in trailing hour
        db.BenchmarkRuns.Add(CreateTestRun("Suite1", DateTime.UtcNow.AddMinutes(-40)));
        db.BenchmarkRuns.Add(CreateTestRun("Suite2", DateTime.UtcNow.AddMinutes(-20)));
        // Add 1 old run from 2 hours ago (should not count for hourly)
        db.BenchmarkRuns.Add(CreateTestRun("SuiteOld", DateTime.UtcNow.AddHours(-2)));
        await db.SaveChangesAsync();

        var (allowed1, reason1) = await guard.CanSpendAsync();
        Assert.True(allowed1);
        Assert.Null(reason1);

        // Add 3rd run in trailing hour (hitting the cap of 3)
        db.BenchmarkRuns.Add(CreateTestRun("Suite3", DateTime.UtcNow.AddMinutes(-5)));
        await db.SaveChangesAsync();

        var (allowed2, reason2) = await guard.CanSpendAsync();
        Assert.False(allowed2);
        Assert.NotNull(reason2);
        Assert.Contains("Hourly benchmark run cap reached (3 runs/hour)", reason2);
    }

    [Fact]
    public async Task CanSpendAsync_AllowsBelowDailyCap_DeniesAtDailyCap()
    {
        var db = CreateDbContext();
        var config = CreateConfig(maxRunsPerHour: 10, maxRunsPerDay: 4);
        var guard = new BenchmarkComplianceGuard(config, db);

        // Add 3 runs in past 24 hours (spaced out so hourly cap of 10 is not hit)
        db.BenchmarkRuns.Add(CreateTestRun("S1", DateTime.UtcNow.AddHours(-18)));
        db.BenchmarkRuns.Add(CreateTestRun("S2", DateTime.UtcNow.AddHours(-12)));
        db.BenchmarkRuns.Add(CreateTestRun("S3", DateTime.UtcNow.AddHours(-6)));
        // Add 1 old run from 30 hours ago (should not count for daily)
        db.BenchmarkRuns.Add(CreateTestRun("SOld", DateTime.UtcNow.AddHours(-30)));
        await db.SaveChangesAsync();

        var (allowed1, reason1) = await guard.CanSpendAsync();
        Assert.True(allowed1);
        Assert.Null(reason1);

        // Add 4th run in past 24 hours (hitting the daily cap of 4)
        db.BenchmarkRuns.Add(CreateTestRun("S4", DateTime.UtcNow.AddHours(-2)));
        await db.SaveChangesAsync();

        var (allowed2, reason2) = await guard.CanSpendAsync();
        Assert.False(allowed2);
        Assert.NotNull(reason2);
        Assert.Contains("Daily benchmark run cap reached (4 runs/day)", reason2);
    }

    [Fact]
    public async Task CanSpendAsync_CountsAcrossAllSuitesAndModels()
    {
        var db = CreateDbContext();
        var config = CreateConfig(maxRunsPerHour: 2, maxRunsPerDay: 20);
        var guard = new BenchmarkComplianceGuard(config, db);

        // 1 run on Suite 1 with Model A
        var r1 = CreateTestRun("Suite1", DateTime.UtcNow.AddMinutes(-30));
        r1.TestedModelIdUsed = "model-a";
        db.BenchmarkRuns.Add(r1);

        // 1 run on Suite 2 with Model B
        var r2 = CreateTestRun("Suite2", DateTime.UtcNow.AddMinutes(-10));
        r2.TestedModelIdUsed = "model-b";
        db.BenchmarkRuns.Add(r2);
        await db.SaveChangesAsync();

        var (allowed, reason) = await guard.CanSpendAsync();
        Assert.False(allowed);
        Assert.Contains("Hourly benchmark run cap reached (2 runs/hour)", reason);
    }

    [Fact]
    public async Task CanAddQuestionsAsync_EnforcesSuiteCap()
    {
        var db = CreateDbContext();
        var config = CreateConfig(maxQuestions: 3);
        var guard = new BenchmarkComplianceGuard(config, db);

        var suite = new BenchmarkSuite { Name = "Test Suite" };
        suite.Questions.Add(new BenchmarkQuestion { QuestionText = "Q1", OrderIndex = 1 });
        suite.Questions.Add(new BenchmarkQuestion { QuestionText = "Q2", OrderIndex = 2 });
        db.BenchmarkSuites.Add(suite);
        await db.SaveChangesAsync();

        // Adding 1 question (2+1 = 3 <= 3) is allowed
        var (allowed1, reason1) = await guard.CanAddQuestionsAsync(suite.Id, 1);
        Assert.True(allowed1);
        Assert.Null(reason1);

        // Adding 2 questions (2+2 = 4 > 3) is denied
        var (allowed2, reason2) = await guard.CanAddQuestionsAsync(suite.Id, 2);
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

    private static (AdminBenchmarkController controller, ApplicationDbContext db, BenchmarkComplianceGuard guard) CreateTestBenchmarkController(
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

        // The source and wiki indexes are only reached by the suite-health citation endpoint,
        // which these tests do not exercise — same reason the two nulls above are safe.
        var controller = new AdminBenchmarkController(
            db, benchmarkService, scoringProfileService, runManager, difficultyJobManager, guard, scopeFactory,
            null!, null!)
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

    private static async Task<(BenchmarkSuite suite, SystemAiApiConfiguration modelA, SystemAiApiConfiguration modelB, SystemAiApiConfiguration modelC)> SeedConfigsAndSuite(ApplicationDbContext db)
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
        await db.SaveChangesAsync();

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

        var run = await db.BenchmarkRuns.FirstAsync();
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

        var run = await db.BenchmarkRuns.FirstAsync();
        Assert.False(run.SameProviderAcknowledged); // Cross provider does not require same-provider acknowledgement
        Assert.NotNull(run.PurposeStatementUsed);
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
            TestedModelProviderUsed = "Google",
            TestedModelDisplayNameUsed = "Gemini",
            TestedModelIdUsed = "gemini",
            AssessorModelProviderUsed = "Anthropic",
            AssessorModelDisplayNameUsed = "Claude",
            AssessorModelIdUsed = "claude",
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
        await db.SaveChangesAsync();

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
            TestedModelProviderUsed = "Google",
            TestedModelDisplayNameUsed = "Gemini",
            TestedModelIdUsed = "gemini",
            AssessorModelProviderUsed = "Anthropic",
            AssessorModelDisplayNameUsed = "Claude",
            AssessorModelIdUsed = "claude",
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
        await db.SaveChangesAsync();

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
            TestedModelProviderUsed = "Google",
            TestedModelDisplayNameUsed = "Gemini",
            TestedModelIdUsed = "gemini",
            AssessorModelProviderUsed = "Anthropic",
            AssessorModelDisplayNameUsed = "Claude",
            AssessorModelIdUsed = "claude"
        };
        run1.Answers.Add(new BenchmarkRunAnswer { QuestionText = "Q1", AnswerText = "12345", OrderIndex = 1 });
        run1.Answers.Add(new BenchmarkRunAnswer { QuestionText = "Q2", AnswerText = "67890", OrderIndex = 2 });

        var run2 = new BenchmarkRun
        {
            BenchmarkSuiteId = suite.Id,
            SuiteName = suite.Name,
            TestedModelProviderUsed = "Google",
            TestedModelDisplayNameUsed = "Gemini",
            TestedModelIdUsed = "gemini",
            AssessorModelProviderUsed = "Anthropic",
            AssessorModelDisplayNameUsed = "Claude",
            AssessorModelIdUsed = "claude"
        };
        run2.Answers.Add(new BenchmarkRunAnswer { QuestionText = "Q1", AnswerText = "abc", OrderIndex = 1 });

        db.BenchmarkRuns.AddRange(run1, run2);
        await db.SaveChangesAsync();

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
        await db.SaveChangesAsync();

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
        await db.SaveChangesAsync();

        // Should succeed
        var acceptedResult = await controller.StartRun(request);
        Assert.IsType<AcceptedResult>(acceptedResult);
    }
}
