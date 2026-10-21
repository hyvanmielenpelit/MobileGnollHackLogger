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

/// <summary>
/// The usage of one user by model, role context and thinking level: key sources, the user and range filters, every
/// role group, token normalization, model names and the endpoint's required user id.
/// </summary>
public class AdminTelemetryUserModelsTests
{
    private const string UserId = "u1";

    private static readonly DateTime Day = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);

    private static ApplicationDbContext NewDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
        .Options);

    private static AiTelemetrySummaryService Service(ApplicationDbContext db) => new(db, new ModelMetadataService());

    private static AdminController CreateController(ApplicationDbContext db)
    {
        var keyBytes = new byte[32];
        keyBytes[0] = 77;
        keyBytes[31] = 99;
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "AesEncryptionKey", Convert.ToBase64String(keyBytes) },
                { "AiRateLimitSettings:MaxConcurrentModelCalls", "2" },
                { "AiRateLimitSettings:MaxRetryAfterSeconds", "90" }
            })
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
        return new AdminController(db, config, null!, cryptoService, governor, endpointPolicy, usageGuard, pricingService);
    }

    private static SystemAiUsageLog Usage(string userId = UserId, int roleContext = 1, string modelId = "model-x", int input = 100,
        int cacheRead = 0, int cacheWrite = 0, string? thinkingLevel = "", decimal? cost = null, DateTime? at = null,
        int? durationMs = 100) => new()
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
        CacheCreationInputTokens = cacheWrite,
        ThinkingLevel = thinkingLevel,
        EstimatedCost = cost,
        PricingSource = cost == null ? null : "catalog",
        TotalDurationMs = durationMs
    };

    /// <summary>Own-key rows: role 1 or 2 only, and a non-nullable thinking level.</summary>
    private static UserKeyAiUsageLog UserKeyUsage(string userId = UserId, int roleContext = 1, string modelId = "model-x",
        string thinkingLevel = "", int input = 50, int cacheRead = 0, decimal? cost = null, DateTime? at = null) => new()
    {
        AspNetUserId = userId,
        TimestampUtc = at ?? Day,
        Provider = "Anthropic",
        ModelId = modelId,
        RoleContext = roleContext,
        ThinkingLevel = thinkingLevel,
        InputTokens = input,
        OutputTokens = 5,
        CacheReadInputTokens = cacheRead,
        EstimatedCost = cost,
        PricingSource = cost == null ? null : "catalog"
    };

    private static async Task<AiTelemetryUserModelsDto> UserModelsAsync(ApplicationDbContext db, string? keys = null,
        string userId = UserId, DateTime? start = null, DateTime? end = null)
    {
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return await Service(db).GetUserModelsAsync(start, end, keys, userId, ct: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetUserModels_ReturnsOneRowPerModelRoleAndLevel_OfTheUserOnly()
    {
        using var db = NewDb();
        db.Users.AddRange(new ApplicationUser { Id = UserId, UserName = "alice" }, new ApplicationUser { Id = "u2", UserName = "bob" });
        db.SystemAiUsageLogs.AddRange(
            Usage(thinkingLevel: "low", cost: 0.10m),
            Usage(thinkingLevel: "low", cost: 0.20m),
            Usage(thinkingLevel: "high", cost: 0.50m),
            Usage(modelId: "model-y", durationMs: null),
            Usage("u2", thinkingLevel: "low", cost: 9m),
            Usage("u2", modelId: "model-z"));

        var result = await UserModelsAsync(db);

        Assert.Equal("system", result.KeySource);
        Assert.Equal(UserId, result.UserId);
        Assert.Equal("alice", result.UserName);
        Assert.Equal(3, result.Rows.Count);
        Assert.DoesNotContain(result.Rows, r => r.ModelId == "model-z");

        var low = Assert.Single(result.Rows, r => r.ModelId == "model-x" && r.ThinkingLevel == "low");
        Assert.Equal("system", low.KeySource);
        Assert.Equal("Anthropic", low.Provider);
        Assert.Equal(2, low.Requests);
        Assert.Equal(0.30m, low.EstimatedCost);
        Assert.Equal(2, low.PricedRequests);
        Assert.Equal(20, low.OutputTokens);
        Assert.Equal(200, low.DurationSumMs);
        Assert.Equal(2, low.DurationCount);
        Assert.Equal(Day, low.LastRequestUtc);
        Assert.Equal(DateTimeKind.Utc, low.LastRequestUtc!.Value.Kind);

        var y = Assert.Single(result.Rows, r => r.ModelId == "model-y");
        Assert.Equal(0, y.DurationCount);
        Assert.Equal(0, y.PricedRequests);

        // The wire order is cost, highest first.
        Assert.Equal(new[] { 0.50m, 0.30m, 0m }, result.Rows.Select(r => r.EstimatedCost));
    }

    [Fact]
    public async Task GetUserModels_ReadsTheSelectedKeySources()
    {
        using var db = NewDb();
        db.SystemAiUsageLogs.Add(Usage(cost: 0.20m));
        db.UserKeyAiUsageLogs.AddRange(UserKeyUsage(cost: 0.10m), UserKeyUsage("u2", cost: 5m));

        var system = await UserModelsAsync(db, "system");
        var user = await UserModelsAsync(db, "user");
        var both = await UserModelsAsync(db, "both");
        var fallback = await UserModelsAsync(db);

        Assert.Equal("system", system.KeySource);
        Assert.Equal("system", Assert.Single(system.Rows).KeySource);
        Assert.Equal("user", user.KeySource);
        var own = Assert.Single(user.Rows);
        Assert.Equal("user", own.KeySource);
        Assert.Equal(0.10m, own.EstimatedCost);
        Assert.Equal("both", both.KeySource);
        // The same model, role and level appear once per key source.
        Assert.Equal(new[] { "system", "user" }, both.Rows.Select(r => r.KeySource));
        Assert.Equal("system", fallback.KeySource);
        Assert.Single(fallback.Rows);
    }

    [Fact]
    public async Task GetUserModels_TheRangeIncludesTheWholeEndDay()
    {
        using var db = NewDb();
        db.SystemAiUsageLogs.AddRange(
            Usage(modelId: "in-range", at: new DateTime(2026, 9, 10, 23, 59, 0, DateTimeKind.Utc)),
            Usage(modelId: "next-day", at: new DateTime(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc)),
            Usage(modelId: "before", at: new DateTime(2026, 9, 8, 23, 59, 0, DateTimeKind.Utc)));
        db.UserKeyAiUsageLogs.AddRange(
            UserKeyUsage(modelId: "own-in-range", at: new DateTime(2026, 9, 9, 0, 0, 0, DateTimeKind.Utc)),
            UserKeyUsage(modelId: "own-next-day", at: new DateTime(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc)));

        var result = await UserModelsAsync(db, "both", start: new DateTime(2026, 9, 9), end: new DateTime(2026, 9, 10));

        Assert.Equal(new[] { "in-range", "own-in-range" }, result.Rows.Select(r => r.ModelId).OrderBy(m => m, StringComparer.Ordinal));
    }

    [Fact]
    public async Task GetUserModels_IncludesEveryRoleGroup()
    {
        using var db = NewDb();
        db.SystemAiUsageLogs.AddRange(Usage(roleContext: 1), Usage(roleContext: 2), Usage(roleContext: 4), Usage(roleContext: 99));
        db.UserKeyAiUsageLogs.AddRange(UserKeyUsage(roleContext: 1), UserKeyUsage(roleContext: 2));

        var system = await UserModelsAsync(db, "system");
        var user = await UserModelsAsync(db, "user");

        Assert.Equal(new[] { "benchmark", "chat", "other", "title" }, system.Rows.Select(r => r.RoleGroup).OrderBy(g => g, StringComparer.Ordinal));
        Assert.Equal(new[] { "chat", "title" }, user.Rows.Select(r => r.RoleGroup).OrderBy(g => g, StringComparer.Ordinal));
    }

    [Fact]
    public async Task GetUserModels_KeepsNotRecordedApartFromNoneSent()
    {
        using var db = NewDb();
        db.SystemAiUsageLogs.AddRange(Usage(thinkingLevel: null), Usage(thinkingLevel: ""), Usage(thinkingLevel: ""));
        db.UserKeyAiUsageLogs.Add(UserKeyUsage(thinkingLevel: ""));

        var result = await UserModelsAsync(db, "both");

        Assert.Equal(3, result.Rows.Count);
        Assert.Equal(1, Assert.Single(result.Rows, r => r.KeySource == "system" && r.ThinkingLevel == null).Requests);
        Assert.Equal(2, Assert.Single(result.Rows, r => r.KeySource == "system" && r.ThinkingLevel == "").Requests);
        Assert.Equal(1, Assert.Single(result.Rows, r => r.KeySource == "user" && r.ThinkingLevel == "").Requests);
    }

    [Fact]
    public async Task GetUserModels_NormalizesInputTokensPerRole()
    {
        using var db = NewDb();
        db.SystemAiUsageLogs.AddRange(
            Usage(roleContext: 4, input: 1000, cacheRead: 800, cacheWrite: 50),
            Usage(roleContext: 1, input: 200, cacheRead: 800, cacheWrite: 50));
        db.UserKeyAiUsageLogs.Add(UserKeyUsage(roleContext: 1, input: 300, cacheRead: 700));

        var result = await UserModelsAsync(db, "both");

        var benchmark = Assert.Single(result.Rows, r => r.RoleContext == 4);
        Assert.Equal(1000, benchmark.PromptTokens);
        Assert.Equal(150, benchmark.UncachedInputTokens);
        Assert.Equal(800, benchmark.CacheReadTokens);
        Assert.Equal(50, benchmark.CacheCreationTokens);

        // Chat rows store the prompt minus cache reads.
        var chat = Assert.Single(result.Rows, r => r.RoleContext == 1 && r.KeySource == "system");
        Assert.Equal(1000, chat.PromptTokens);
        Assert.Equal(150, chat.UncachedInputTokens);
        Assert.Equal(1000, Assert.Single(result.Rows, r => r.KeySource == "user").PromptTokens);
    }

    [Fact]
    public async Task GetUserModels_NamesCatalogModels_AndLeavesUnknownOnesUnnamed()
    {
        var target = new ModelMetadataService().GetCatalogTargets("Anthropic")[0];
        using var db = NewDb();
        db.SystemAiUsageLogs.AddRange(Usage(modelId: target.ModelId), Usage(modelId: "model-x"));

        var result = await UserModelsAsync(db);

        Assert.Equal(target.DisplayName, Assert.Single(result.Rows, r => r.ModelId == target.ModelId).ModelName);
        Assert.Null(Assert.Single(result.Rows, r => r.ModelId == "model-x").ModelName);
    }

    [Fact]
    public async Task GetUserModels_DatesCostFromTheEarliestPricedRowOfEachIncludedLog()
    {
        using var db = NewDb();
        db.SystemAiUsageLogs.AddRange(
            Usage(cost: null, at: Day.AddDays(-4)),
            Usage("u2", cost: 0.10m, at: Day.AddDays(-2)),
            Usage(cost: 0.25m, at: Day));
        db.UserKeyAiUsageLogs.Add(UserKeyUsage("u3", cost: 0.10m, at: Day.AddDays(-3)));

        var system = await UserModelsAsync(db, "system");
        var both = await UserModelsAsync(db, "both");

        // Range only, not the user: another user's priced row dates the column.
        Assert.Equal(Day.AddDays(-2), system.CostRecordedSinceUtc);
        Assert.Equal(DateTimeKind.Utc, system.CostRecordedSinceUtc!.Value.Kind);
        Assert.Equal(Day.AddDays(-3), both.CostRecordedSinceUtc);
    }

    [Fact]
    public async Task GetUserModels_AGoneAccountHasNoName()
    {
        using var db = NewDb();
        db.SystemAiUsageLogs.Add(Usage("gone"));

        var result = await UserModelsAsync(db, userId: "gone");

        Assert.Equal("gone", result.UserId);
        Assert.Null(result.UserName);
        Assert.Single(result.Rows);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetAiTelemetryUserModels_RequiresUserId(string? userId)
    {
        using var db = NewDb();
        var controller = CreateController(db);

        var result = await controller.GetAiTelemetryUserModels(null, null, "both", userId, Service(db));

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var message = bad.Value!.GetType().GetProperty("message")!.GetValue(bad.Value) as string;
        Assert.Equal("userId is required.", message);
    }

    [Fact]
    public async Task GetAiTelemetryUserModels_PassesTheRangeKeysAndUser()
    {
        using var db = NewDb();
        db.SystemAiUsageLogs.Add(Usage(modelId: "system-model"));
        db.UserKeyAiUsageLogs.AddRange(
            UserKeyUsage(modelId: "own-model"),
            UserKeyUsage(modelId: "own-early", at: Day.AddDays(-5)),
            UserKeyUsage("u2", modelId: "other-user"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var controller = CreateController(db);

        var result = await controller.GetAiTelemetryUserModels(Day.Date.AddDays(-1), Day.Date, "user", UserId, Service(db));

        var dto = Assert.IsType<AiTelemetryUserModelsDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("user", dto.KeySource);
        Assert.Equal(UserId, dto.UserId);
        Assert.Equal("own-model", Assert.Single(dto.Rows).ModelId);
    }
}
