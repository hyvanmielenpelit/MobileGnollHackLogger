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

public class AdminTelemetryModelUsersTests
{
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

    private static SystemAiUsageLog Usage(string userId, int roleContext = 1, string modelId = "model-x", int input = 100,
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

    private static UserKeyAiUsageLog UserKeyUsage(string userId, int roleContext = 1, string modelId = "model-x",
        string thinkingLevel = "", DateTime? at = null) => new()
    {
        AspNetUserId = userId,
        TimestampUtc = at ?? Day,
        Provider = "Anthropic",
        ModelId = modelId,
        RoleContext = roleContext,
        ThinkingLevel = thinkingLevel,
        InputTokens = 50,
        OutputTokens = 5
    };

    private static async Task<AiTelemetryModelUsersDto> ModelUsersAsync(ApplicationDbContext db, string? keys = null,
        string modelId = "model-x", IReadOnlyCollection<string>? roles = null, DateTime? start = null, DateTime? end = null)
    {
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return await Service(db).GetModelUsersAsync(start, end, keys, "Anthropic", modelId, roles, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetModelUsers_ReturnsOnlyTheRequestedModel_OneRowPerUserRoleAndLevel()
    {
        using var db = NewDb();
        db.Users.AddRange(new ApplicationUser { Id = "u1", UserName = "alice" }, new ApplicationUser { Id = "u2", UserName = "bob" });
        db.SystemAiUsageLogs.AddRange(
            Usage("u1", thinkingLevel: "low", cost: 0.10m),
            Usage("u1", thinkingLevel: "low", cost: 0.20m),
            Usage("u1", thinkingLevel: "high", cost: 0.50m),
            Usage("u1", roleContext: 2, thinkingLevel: "low"),
            Usage("u2", thinkingLevel: "low", cost: 0.05m, durationMs: null),
            Usage("u1", modelId: "model-y"),
            Usage("u2", modelId: "model-y"));

        var result = await ModelUsersAsync(db);

        Assert.Equal("system", result.KeySource);
        Assert.Equal("Anthropic", result.Provider);
        Assert.Equal("model-x", result.ModelId);
        Assert.Equal(4, result.Rows.Count);

        var aliceLow = Assert.Single(result.Rows, r => r.UserId == "u1" && r.RoleContext == 1 && r.ThinkingLevel == "low");
        Assert.Equal(2, aliceLow.Requests);
        Assert.Equal(0.30m, aliceLow.EstimatedCost);
        Assert.Equal(2, aliceLow.PricedRequests);
        Assert.Equal(20, aliceLow.OutputTokens);
        Assert.Equal(200, aliceLow.DurationSumMs);
        Assert.Equal(2, aliceLow.DurationCount);
        Assert.Equal("chat", aliceLow.RoleGroup);
        Assert.Equal("alice", aliceLow.UserName);

        Assert.Equal("title", Assert.Single(result.Rows, r => r.UserId == "u1" && r.RoleContext == 2).RoleGroup);

        var bob = Assert.Single(result.Rows, r => r.UserId == "u2");
        Assert.Equal(0, bob.DurationCount);

        // The wire order is cost, highest first.
        Assert.Equal(0.50m, result.Rows[0].EstimatedCost);
    }

    [Fact]
    public async Task GetModelUsers_UserKeysReadOnlyTheUserKeyLog_AndSystemOnlyTheSystemLog()
    {
        using var db = NewDb();
        db.SystemAiUsageLogs.Add(Usage("system-user"));
        db.UserKeyAiUsageLogs.Add(UserKeyUsage("key-user"));

        var user = await ModelUsersAsync(db, "user");
        var system = await ModelUsersAsync(db, "system");
        var both = await ModelUsersAsync(db, "both");

        Assert.Equal("user", user.KeySource);
        Assert.Equal("key-user", Assert.Single(user.Rows).UserId);
        Assert.Equal("system", system.KeySource);
        Assert.Equal("system-user", Assert.Single(system.Rows).UserId);
        Assert.Equal("system", both.KeySource);
        Assert.Equal("system-user", Assert.Single(both.Rows).UserId);
    }

    [Fact]
    public async Task GetModelUsers_FiltersByRoleGroup_AndDefaultsToTheNamedGroups()
    {
        using var db = NewDb();
        db.SystemAiUsageLogs.AddRange(Usage("u1", roleContext: 1), Usage("u1", roleContext: 2), Usage("u1", roleContext: 4),
            Usage("u1", roleContext: 99));

        var title = await ModelUsersAsync(db, roles: new[] { "title" });
        var defaults = await ModelUsersAsync(db);
        var other = await ModelUsersAsync(db, roles: new[] { "OTHER" });

        Assert.Equal("title", Assert.Single(title.Rows).RoleGroup);
        Assert.Equal(new[] { "title" }, title.Roles);
        Assert.Equal(new[] { "benchmark", "chat", "title" }, defaults.Rows.Select(r => r.RoleGroup).OrderBy(g => g, StringComparer.Ordinal));
        Assert.Equal(new[] { "chat", "title", "benchmark" }, defaults.Roles);
        Assert.Equal("other", Assert.Single(other.Rows).RoleGroup);
    }

    [Fact]
    public async Task GetModelUsers_KeepsNotRecordedApartFromNoneSent()
    {
        using var db = NewDb();
        db.SystemAiUsageLogs.AddRange(Usage("u1", thinkingLevel: null), Usage("u1", thinkingLevel: ""), Usage("u1", thinkingLevel: ""));

        var result = await ModelUsersAsync(db);

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(1, Assert.Single(result.Rows, r => r.ThinkingLevel == null).Requests);
        Assert.Equal(2, Assert.Single(result.Rows, r => r.ThinkingLevel == "").Requests);
    }

    [Fact]
    public async Task GetModelUsers_NormalizesInputTokensPerRole_AsTheSummaryDoes()
    {
        using var db = NewDb();
        db.SystemAiUsageLogs.AddRange(
            Usage("u1", roleContext: 4, input: 1000, cacheRead: 800, cacheWrite: 50),
            Usage("u1", roleContext: 1, input: 200, cacheRead: 800, cacheWrite: 50));

        var result = await ModelUsersAsync(db);
        var summary = await Service(db).GetSummaryAsync(null, null, "system", TestContext.Current.CancellationToken);

        foreach (int role in new[] { 1, 4 })
        {
            var row = Assert.Single(result.Rows, r => r.RoleContext == role);
            var summaryRow = Assert.Single(summary.Rows, r => r.RoleContext == role);
            Assert.Equal(summaryRow.PromptTokens, row.PromptTokens);
            Assert.Equal(summaryRow.UncachedInputTokens, row.UncachedInputTokens);
            Assert.Equal(summaryRow.CacheReadTokens, row.CacheReadTokens);
            Assert.Equal(summaryRow.CacheCreationTokens, row.CacheCreationTokens);
        }
        Assert.Equal(1000, Assert.Single(result.Rows, r => r.RoleContext == 4).PromptTokens);
        Assert.Equal(1000, Assert.Single(result.Rows, r => r.RoleContext == 1).PromptTokens);
    }

    [Fact]
    public async Task GetModelUsers_TheRangeIncludesTheWholeEndDay()
    {
        using var db = NewDb();
        var endDay = new DateTime(2026, 9, 10);
        db.SystemAiUsageLogs.AddRange(
            Usage("in-range", at: new DateTime(2026, 9, 10, 23, 59, 0, DateTimeKind.Utc)),
            Usage("next-day", at: new DateTime(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc)),
            Usage("before", at: new DateTime(2026, 9, 8, 23, 59, 0, DateTimeKind.Utc)));

        var result = await ModelUsersAsync(db, start: new DateTime(2026, 9, 9), end: endDay);

        Assert.Equal("in-range", Assert.Single(result.Rows).UserId);
    }

    [Fact]
    public async Task GetModelUsers_LastRequestIsTheLatestRowInUtc_AndTheNameIsCurrent()
    {
        using var db = NewDb();
        var user = new ApplicationUser { Id = "u1", UserName = "old-name" };
        db.Users.Add(user);
        db.SystemAiUsageLogs.AddRange(Usage("u1", at: Day.AddHours(-3)), Usage("u1", at: Day), Usage("u1", at: Day.AddHours(-1)),
            Usage("gone"));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        user.UserName = "renamed";

        var result = await ModelUsersAsync(db);

        var row = Assert.Single(result.Rows, r => r.UserId == "u1");
        Assert.Equal(Day, row.LastRequestUtc);
        Assert.Equal(DateTimeKind.Utc, row.LastRequestUtc!.Value.Kind);
        Assert.Equal("renamed", row.UserName);
        Assert.Null(Assert.Single(result.Rows, r => r.UserId == "gone").UserName);
    }

    [Fact]
    public async Task GetModelUsers_DatesCostFromTheEarliestPricedRowOfTheLog()
    {
        using var db = NewDb();
        db.SystemAiUsageLogs.AddRange(
            Usage("u1", cost: null, at: Day.AddDays(-3)),
            Usage("u1", modelId: "model-y", cost: 0.10m, at: Day.AddDays(-2)),
            Usage("u1", cost: 0.25m, at: Day));

        var result = await ModelUsersAsync(db);

        Assert.Equal(Day.AddDays(-2), result.CostRecordedSinceUtc);
        Assert.Equal(DateTimeKind.Utc, result.CostRecordedSinceUtc!.Value.Kind);
    }

    [Theory]
    [InlineData(null, "model-x")]
    [InlineData("Anthropic", null)]
    [InlineData(" ", "model-x")]
    [InlineData("Anthropic", "")]
    public async Task GetAiTelemetryModelUsers_RequiresProviderAndModel(string? provider, string? modelId)
    {
        using var db = NewDb();
        var controller = CreateController(db);

        var result = await controller.GetAiTelemetryModelUsers(null, null, null, provider, modelId, null, Service(db));

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GetAiTelemetryModelUsers_ParsesRolesAndKeys()
    {
        using var db = NewDb();
        db.UserKeyAiUsageLogs.AddRange(UserKeyUsage("u1", roleContext: 1), UserKeyUsage("u1", roleContext: 2),
            UserKeyUsage("u1", roleContext: 4));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var controller = CreateController(db);

        var result = await controller.GetAiTelemetryModelUsers(null, null, "user", "Anthropic", "model-x", "chat, benchmark", Service(db));

        var dto = Assert.IsType<AiTelemetryModelUsersDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("user", dto.KeySource);
        Assert.Equal(new[] { "benchmark", "chat" }, dto.Rows.Select(r => r.RoleGroup).OrderBy(g => g, StringComparer.Ordinal));
    }
}
