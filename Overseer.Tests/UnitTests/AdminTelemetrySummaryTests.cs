using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.Providers;
using Overseer.Services.Telemetry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class AdminTelemetrySummaryTests
{
    private static (AdminController controller, ApplicationDbContext db) CreateTestController()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var db = new ApplicationDbContext(dbOptions);

        var keyBytes = new byte[32];
        keyBytes[0] = 77;
        keyBytes[31] = 99;
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "AesEncryptionKey", Convert.ToBase64String(keyBytes) },
            { "AiRateLimitSettings:MaxConcurrentModelCalls", "2" },
            { "AiRateLimitSettings:MaxRetryAfterSeconds", "90" }
        };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var cryptoService = new CryptoService(config);
        var governor = new AiRequestGovernor(config, NullLogger<AiRequestGovernor>.Instance);
        var metadataService = new ModelMetadataService();
        var pricingService = new ModelPricingService(metadataService, db);
        var endpointPolicy = new Overseer.Services.Privacy.EndpointPolicy(config);
        var usageGuard = new SystemConfigUsageGuard(
            db,
            new Overseer.Services.Benchmarking.BenchmarkDifficultyJobManager(),
            new Overseer.Services.Benchmarking.BenchmarkGenerationJobManager(),
            new Overseer.Services.Benchmarking.BenchmarkRubricCheckJobManager(),
            new Overseer.Services.Benchmarking.BenchmarkRubricGapAuthorJobManager(),
            new Overseer.Services.Benchmarking.BenchmarkReportPackJobManager());
        var controller = new AdminController(db, config, null!, cryptoService, governor, endpointPolicy, usageGuard, pricingService);

        return (controller, db);
    }

    /// <summary>
    /// Model X: one request, 200 uncached + 800 cache-read input tokens, 1000 ms.
    /// Model Y: three requests, 100 uncached input tokens each, 100 ms each.
    /// </summary>
    private static async Task SeedUsageAsync(ApplicationDbContext db)
    {
        const string userId = "telemetry-user";
        db.Users.Add(new ApplicationUser { Id = userId, UserName = "telemetry-user" });
        db.SystemAiApiConfigurations.Add(new SystemAiApiConfiguration
        {
            Id = 1,
            DisplayName = "Telemetry Model",
            Provider = "Anthropic",
            ModelId = "model-x"
        });

        var timestamp = new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);
        db.SystemAiUsageLogs.Add(new SystemAiUsageLog
        {
            SystemAiApiConfigurationId = 1,
            AspNetUserId = userId,
            TimestampUtc = timestamp,
            Provider = "Anthropic",
            ModelId = "model-x",
            InputTokens = 200,
            OutputTokens = 10,
            CacheReadInputTokens = 800,
            CacheCreationInputTokens = 0,
            TotalDurationMs = 1000
        });
        for (var i = 0; i < 3; i++)
        {
            db.SystemAiUsageLogs.Add(new SystemAiUsageLog
            {
                SystemAiApiConfigurationId = 1,
                AspNetUserId = userId,
                TimestampUtc = timestamp,
                Provider = "Anthropic",
                ModelId = "model-y",
                InputTokens = 100,
                OutputTokens = 10,
                CacheReadInputTokens = 0,
                CacheCreationInputTokens = 0,
                TotalDurationMs = 100
            });
        }
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<AiTelemetrySummaryDto> GetSummaryAsync()
    {
        var (controller, db) = CreateTestController();
        await SeedUsageAsync(db);

        var result = await controller.GetAiTelemetrySummary(null, null, null,
            new Overseer.Services.Telemetry.AiTelemetrySummaryService(db, new ModelMetadataService()));

        var ok = Assert.IsType<OkObjectResult>(result);
        return Assert.IsType<AiTelemetrySummaryDto>(ok.Value);
    }

    [Fact]
    public async Task GetAiTelemetrySummary_CacheHitRatio_IsShareOfWholePrompt()
    {
        var summary = await GetSummaryAsync();

        var modelX = Assert.Single(summary.Rows, m => m.ModelId == "model-x");
        Assert.Equal(1000, modelX.PromptTokens);
        Assert.Equal(0.8, (double)modelX.CacheReadTokens / modelX.PromptTokens);
        Assert.Equal(1300, summary.TotalPromptTokens);
        Assert.Equal(Math.Round(800.0 / 1300.0, 4), summary.CacheHitRatio);
    }

    [Fact]
    public async Task GetAiTelemetrySummary_AvgDuration_IsWeightedByRequests()
    {
        var summary = await GetSummaryAsync();

        Assert.Equal(4, summary.TotalRequests);
        Assert.Equal(325, summary.AvgDurationMs);
        var modelY = Assert.Single(summary.Rows, m => m.ModelId == "model-y");
        Assert.Equal(100, modelY.DurationSumMs / modelY.DurationCount);
    }

    // --- Service-level facts -------------------------------------------------------------------

    private static readonly DateTime Day = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);

    private static ApplicationDbContext NewDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
        .Options);

    private static AiTelemetrySummaryService Service(ApplicationDbContext db) => new(db, new ModelMetadataService());

    private static SystemAiUsageLog Usage(int roleContext, string modelId = "model-x", int input = 100, int cacheRead = 0,
        string? thinkingLevel = "", decimal? cost = null, DateTime? at = null, string userId = "telemetry-user") => new()
    {
        SystemAiApiConfigurationId = 1,
        AspNetUserId = userId,
        TimestampUtc = at ?? Day,
        Provider = "Anthropic",
        ModelId = modelId,
        RoleContext = roleContext,
        InputTokens = input,
        OutputTokens = 10,
        CacheReadInputTokens = cacheRead,
        CacheCreationInputTokens = 0,
        ThinkingLevel = thinkingLevel,
        EstimatedCost = cost,
        PricingSource = cost == null ? null : "catalog",
        TotalDurationMs = 100
    };

    private static ChatProviderAttempt Attempt(DateTime at, Guid run, int roleContext, int callIndex, byte attemptIndex,
        ProviderAttemptOutcome outcome, bool retried, string modelId = "model-x") => new()
    {
        StartedAtUtc = at,
        AgentRunId = run,
        RoleContext = roleContext,
        AgentDepth = 0,
        KeySource = AiKeySource.System,
        SystemAiApiConfigurationId = 1,
        Provider = "Anthropic",
        RequestedModelId = modelId,
        ThinkingLevel = string.Empty,
        CallIndex = callIndex,
        AttemptIndex = attemptIndex,
        DurationMs = 100,
        Outcome = outcome,
        Retried = retried
    };

    private static async Task<AiTelemetrySummaryDto> SummarizeAsync(ApplicationDbContext db, string? keys = null,
        DateTime? start = null, DateTime? end = null)
    {
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return await Service(db).GetSummaryAsync(start, end, keys, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetSummary_GroupsRoleContextsIntoChatTitleBenchmarkAndOther()
    {
        using var db = NewDb();
        db.SystemAiUsageLogs.AddRange(Usage(1), Usage(2), Usage(4), Usage(6), Usage(99));

        var summary = await SummarizeAsync(db);

        Assert.Equal(5, summary.TotalRequests);
        Assert.Equal(1, summary.TotalChatRequests);
        Assert.Equal(1, summary.TotalTitleRequests);
        Assert.Equal(2, summary.TotalBenchmarkRequests);
        Assert.Equal("chat", Assert.Single(summary.Rows, r => r.RoleContext == 1).RoleGroup);
        Assert.Equal("title", Assert.Single(summary.Rows, r => r.RoleContext == 2).RoleGroup);
        Assert.Equal("benchmark", Assert.Single(summary.Rows, r => r.RoleContext == 4).RoleGroup);
        Assert.Equal("benchmark", Assert.Single(summary.Rows, r => r.RoleContext == 6).RoleGroup);
        Assert.Equal("other", Assert.Single(summary.Rows, r => r.RoleContext == 99).RoleGroup);
    }

    [Fact]
    public async Task GetSummary_NormalizesInputTokensPerRole()
    {
        using var db = NewDb();
        db.SystemAiUsageLogs.AddRange(
            Usage(4, modelId: "bench-model", input: 1000, cacheRead: 800),
            Usage(1, modelId: "chat-model", input: 200, cacheRead: 800));

        var summary = await SummarizeAsync(db);

        // A benchmark row stores the whole prompt; a chat row stores the prompt minus cache reads.
        var bench = Assert.Single(summary.Rows, r => r.ModelId == "bench-model");
        Assert.Equal(1000, bench.PromptTokens);
        Assert.Equal(200, bench.UncachedInputTokens);

        var chat = Assert.Single(summary.Rows, r => r.ModelId == "chat-model");
        Assert.Equal(1000, chat.PromptTokens);
        Assert.Equal(200, chat.UncachedInputTokens);
        Assert.Equal(2000, summary.TotalPromptTokens);
    }

    [Fact]
    public async Task GetSummary_SplitsRowsByThinkingLevel_AndKeepsNullApartFromEmpty()
    {
        using var db = NewDb();
        db.SystemAiUsageLogs.AddRange(
            Usage(1, thinkingLevel: "high"),
            Usage(1, thinkingLevel: "low"),
            Usage(1, thinkingLevel: "low"),
            Usage(1, thinkingLevel: null),
            Usage(1, thinkingLevel: ""));

        var summary = await SummarizeAsync(db);

        Assert.Equal(4, summary.Rows.Count);
        Assert.Equal(1, Assert.Single(summary.Rows, r => r.ThinkingLevel == "high").Requests);
        Assert.Equal(2, Assert.Single(summary.Rows, r => r.ThinkingLevel == "low").Requests);
        Assert.Equal(1, Assert.Single(summary.Rows, r => r.ThinkingLevel == null).Requests);
        Assert.Equal(1, Assert.Single(summary.Rows, r => r.ThinkingLevel == "").Requests);
    }

    [Fact]
    public async Task GetSummary_NamesCatalogModels_AndLeavesUnknownOnesUnnamed()
    {
        var target = new ModelMetadataService().GetCatalogTargets("Anthropic")[0];
        using var db = NewDb();
        db.SystemAiUsageLogs.AddRange(Usage(1, modelId: target.ModelId), Usage(1, modelId: "model-x"));

        var summary = await SummarizeAsync(db);

        Assert.Equal(target.DisplayName, Assert.Single(summary.Rows, r => r.ModelId == target.ModelId).ModelName);
        Assert.Null(Assert.Single(summary.Rows, r => r.ModelId == "model-x").ModelName);
    }

    [Fact]
    public async Task GetSummary_SumsPricedRowsOnly_AndDatesCostFromTheEarliestPricedRow()
    {
        using var db = NewDb();
        db.SystemAiUsageLogs.AddRange(
            Usage(1, cost: null, at: Day.AddDays(-2)),
            Usage(1, cost: 0.25m, at: Day.AddDays(-1)),
            Usage(1, cost: 0.50m, at: Day));

        var summary = await SummarizeAsync(db);

        Assert.Equal(3, summary.TotalRequests);
        Assert.Equal(0.75m, summary.TotalEstimatedCost);
        Assert.Equal(2, summary.PricedRequests);
        Assert.Equal(Day.AddDays(-1), summary.CostRecordedSinceUtc);
        Assert.Equal(DateTimeKind.Utc, summary.CostRecordedSinceUtc!.Value.Kind);
    }

    [Theory]
    [InlineData("system", true, false)]
    [InlineData("user", false, true)]
    [InlineData("both", true, true)]
    [InlineData(null, true, false)]
    public async Task GetSummary_FiltersByKeySource(string? keys, bool expectSystem, bool expectUser)
    {
        using var db = NewDb();
        db.Users.Add(new ApplicationUser { Id = "key-user", UserName = "current-name" });
        db.SystemAiUsageLogs.Add(Usage(1, modelId: "system-model"));
        db.UserKeyAiUsageLogs.Add(new UserKeyAiUsageLog
        {
            AspNetUserId = "key-user",
            TimestampUtc = Day,
            Provider = "OpenAI",
            ModelId = "user-model",
            ThinkingLevel = "low",
            InputTokens = 50,
            OutputTokens = 5,
            RoleContext = 1
        });

        var summary = await SummarizeAsync(db, keys);

        Assert.Equal(keys ?? "system", summary.KeySource);
        Assert.Equal(expectSystem, summary.Rows.Any(r => r.KeySource == "system" && r.ModelId == "system-model"));
        Assert.Equal(expectUser, summary.Rows.Any(r => r.KeySource == "user" && r.ModelId == "user-model"));
        Assert.All(summary.Rows.Where(r => r.KeySource == "system"), r => Assert.Null(r.UserId));

        if (expectUser)
        {
            var userRow = Assert.Single(summary.Rows, r => r.KeySource == "user");
            Assert.Equal("key-user", userRow.UserId);
            Assert.Equal("current-name", userRow.UserName);
        }
    }

    [Fact]
    public async Task GetSummary_UserRowsCarryTheUsersCurrentName()
    {
        using var db = NewDb();
        var user = new ApplicationUser { Id = "key-user", UserName = "old-name" };
        db.Users.Add(user);
        db.UserKeyAiUsageLogs.Add(new UserKeyAiUsageLog
        {
            AspNetUserId = "key-user", TimestampUtc = Day, Provider = "OpenAI", ModelId = "user-model", RoleContext = 1
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        user.UserName = "renamed";

        var summary = await SummarizeAsync(db, "user");

        Assert.Equal("renamed", Assert.Single(summary.Rows).UserName);
    }

    /// <summary>
    /// Two 10-minute blocks. Turn 1 (block 1): overloaded, then Ok on the retry. Turn 2 (block 2): call 0 Ok,
    /// call 1 a final server error. Turn 3 (block 2): a final OurCanceled. A title: a final 401 (OurKey).
    /// </summary>
    private static void SeedLedger(ApplicationDbContext db)
    {
        var block1 = new DateTime(2026, 9, 10, 10, 2, 0, DateTimeKind.Utc);
        var block2 = new DateTime(2026, 9, 10, 10, 23, 0, DateTimeKind.Utc);
        Guid turn1 = Guid.NewGuid(), turn2 = Guid.NewGuid(), turn3 = Guid.NewGuid(), title = Guid.NewGuid();

        db.ChatProviderAttempts.AddRange(
            Attempt(block1, turn1, 1, 0, 0, ProviderAttemptOutcome.ProviderOverloaded, retried: true),
            Attempt(block1.AddSeconds(5), turn1, 1, 0, 1, ProviderAttemptOutcome.Ok, retried: false),
            Attempt(block2, turn2, 1, 0, 0, ProviderAttemptOutcome.Ok, retried: false),
            Attempt(block2.AddSeconds(10), turn2, 1, 1, 0, ProviderAttemptOutcome.ProviderServerError, retried: false),
            Attempt(block2.AddSeconds(20), turn3, 1, 0, 0, ProviderAttemptOutcome.OurCanceled, retried: false),
            Attempt(block2.AddSeconds(30), title, 2, 0, 0, ProviderAttemptOutcome.OurKey, retried: false));
    }

    [Fact]
    public async Task GetSummary_ReadsChatAvailabilityFromTheLedger()
    {
        using var db = NewDb();
        db.SystemAiUsageLogs.Add(Usage(1));
        SeedLedger(db);

        var summary = await SummarizeAsync(db);
        var availability = summary.ChatAvailability;

        Assert.True(availability.Recorded);
        Assert.Equal(new DateTime(2026, 9, 10, 10, 2, 0, DateTimeKind.Utc), availability.RecordedSinceUtc);

        Assert.Equal(4, availability.ValidAttempts);
        Assert.Equal(2, availability.ProviderErrors);
        Assert.Equal(0, availability.UnattributedErrors);
        Assert.Equal(2, availability.OurSideFailures);
        Assert.Equal(0.5, availability.ProviderErrorRate);
        Assert.Equal(0.5, availability.InclusiveRate);
        Assert.NotNull(availability.Low);
        Assert.NotNull(availability.High);
        Assert.True(availability.Low <= 0.5 && availability.High >= 0.5);
        Assert.Equal(1, availability.Overloaded);
        Assert.Equal(1, availability.ServerErrors);

        Assert.Equal(5, availability.Calls);
        Assert.Equal(1, availability.FirstAttemptSuccessCalls);
        Assert.Equal(3, availability.FailedCalls);
        Assert.Equal(3, availability.ChatTurns);
        Assert.Equal(1, availability.ChatTurnsEndedByProviderError);

        Assert.Equal(0, availability.TitleValidAttempts);
        Assert.Equal(0, availability.TitleProviderErrors);
        Assert.Equal(1, availability.TitleRequests);
        Assert.Equal(1, availability.TitleRequestsFailed);

        // The chat attempts merge into the usage row of the same model, role and thinking level.
        var chatRow = Assert.Single(summary.Rows, r => r.RoleContext == 1 && r.ModelId == "model-x");
        Assert.Equal(1, chatRow.Requests);
        Assert.Equal(4, chatRow.ValidAttempts);
        Assert.Equal(2, chatRow.ProviderErrors);
        Assert.Equal(1, chatRow.OurSideFailures);
    }

    [Fact]
    public async Task GetSummary_AModelThatOnlyFailedGetsARowWithNoRequests()
    {
        using var db = NewDb();
        db.SystemAiUsageLogs.Add(Usage(1));
        db.ChatProviderAttempts.Add(Attempt(Day, Guid.NewGuid(), 1, 0, 0, ProviderAttemptOutcome.ProviderOverloaded,
            retried: false, modelId: "model-z"));

        var summary = await SummarizeAsync(db);

        var failed = Assert.Single(summary.Rows, r => r.ModelId == "model-z");
        Assert.Equal(0, failed.Requests);
        Assert.Equal(1, failed.ValidAttempts);
        Assert.Equal(1, failed.ProviderErrors);
        Assert.Equal("chat", failed.RoleGroup);
        Assert.Equal("system", failed.KeySource);
    }

    [Fact]
    public async Task GetSummary_AnEmptyLedgerIsNotRecorded()
    {
        using var db = NewDb();
        db.SystemAiUsageLogs.Add(Usage(1));

        var summary = await SummarizeAsync(db);

        Assert.False(summary.ChatAvailability.Recorded);
        Assert.Null(summary.ChatAvailability.RecordedSinceUtc);
        Assert.Null(summary.ChatAvailability.ProviderErrorRate);
        Assert.Null(summary.ChatAvailability.Low);
        Assert.Equal(0, summary.ChatAvailability.Calls);
    }

    [Fact]
    public async Task GetSummary_CountsActivityInTheRange()
    {
        using var db = NewDb();
        db.OverseerDailyActivities.AddRange(
            // u1 was first active before the range, so it is active but not new.
            new OverseerDailyActivity { DateUtc = new DateOnly(2026, 8, 20), AspNetUserId = "u1", ChatsCreated = 1 },
            new OverseerDailyActivity { DateUtc = new DateOnly(2026, 9, 10), AspNetUserId = "u1", ChatsCreated = 2, UserMessages = 3, AssistantMessages = 3 },
            new OverseerDailyActivity { DateUtc = new DateOnly(2026, 9, 11), AspNetUserId = "u2", UserMessages = 1 },
            // Assistant replies alone do not make a user active.
            new OverseerDailyActivity { DateUtc = new DateOnly(2026, 9, 12), AspNetUserId = "u3", AssistantMessages = 1 },
            new OverseerDailyActivity { DateUtc = new DateOnly(2026, 9, 10), AspNetUserId = null, IncognitoChatsCreated = 4, IncognitoUserMessages = 5 });

        var summary = await SummarizeAsync(db, start: new DateTime(2026, 9, 1), end: new DateTime(2026, 9, 30));
        var activity = summary.Activity;

        Assert.Equal(2, activity.ActiveUsers);
        Assert.Equal(1, activity.NewUsers);
        Assert.Equal(new DateOnly(2026, 8, 20), activity.CountedSinceUtc);
        Assert.Equal(2, activity.ChatsCreated);
        Assert.Equal(4, activity.UserMessages);
        Assert.Equal(4, activity.AssistantMessages);
        Assert.Equal(4, activity.IncognitoChatsCreated);
        Assert.Equal(5, activity.IncognitoUserMessages);
    }
}
