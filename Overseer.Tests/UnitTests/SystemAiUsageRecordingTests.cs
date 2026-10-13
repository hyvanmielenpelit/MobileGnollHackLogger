using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Services;
using Overseer.Services.Providers;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// SystemAiUsageLog.AspNetUserId is a required foreign key to AspNetUsers, so a usage row
/// cannot be written for a caller with no user. Attempting it used to leave the rejected
/// row in the change tracker, which then failed every later save on the same context --
/// the failure mode that broke benchmark difficulty assessment.
/// </summary>
public class SystemAiUsageRecordingTests
{
    private const string TestUserId = "11111111-1111-1111-1111-111111111111";

    private static (ApplicationDbContext Db, SystemAiConfigService Service) CreateService()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var db = new ApplicationDbContext(options);

        return (db, new SystemAiConfigService(db, NullLogger<SystemAiConfigService>.Instance));
    }

    private static async Task<SystemAiApiConfiguration> SeedConfigAsync(ApplicationDbContext db)
    {
        var now = DateTime.UtcNow;
        var config = new SystemAiApiConfiguration
        {
            DisplayName = "Gemini 3.7 Flash",
            Provider = "Google",
            ModelId = "gemini-3.7-flash",
            IsEnabled = true,
            IsSystemWide = true,
            ModelRole = 7,
            LastDailyReset = now,
            LastMonthlyReset = now
        };
        db.SystemAiApiConfigurations.Add(config);

        db.Users.Add(new ApplicationUser
        {
            Id = TestUserId,
            UserName = "admin",
            Email = "admin@example.com"
        });

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return config;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task RecordUsageAsync_WithoutUserId_SkipsUsageLogButStillCountsUsage(string? userId)
    {
        var (db, service) = CreateService();
        var config = await SeedConfigAsync(db);

        await service.RecordUsageAsync(config.Id, userId, inputTokens: 1200, outputTokens: 300, roleContext: 1);

        Assert.Empty(db.SystemAiUsageLogs);

        var stored = await db.SystemAiApiConfigurations.FindAsync(
            new object[] { config.Id }, TestContext.Current.CancellationToken);
        Assert.NotNull(stored);
        Assert.Equal(1, stored!.DailyChatRequestsCount);
        Assert.Equal(1500, stored.DailyChatTokensCount);
        Assert.Equal(1500, stored.TotalChatTokensCount);
    }

    [Fact]
    public async Task RecordUsageAsync_WithUserId_WritesOneUsageLogRow()
    {
        var (db, service) = CreateService();
        var config = await SeedConfigAsync(db);

        await service.RecordUsageAsync(
            config.Id,
            TestUserId,
            inputTokens: 900,
            outputTokens: 100,
            roleContext: 4,
            cacheReadTokens: 50,
            cacheCreationTokens: 20,
            totalDurationMs: 4321);

        var log = Assert.Single(db.SystemAiUsageLogs.ToList());
        Assert.Equal(TestUserId, log.AspNetUserId);
        Assert.Equal(config.Id, log.SystemAiApiConfigurationId);
        Assert.Equal("Google", log.Provider);
        Assert.Equal("gemini-3.7-flash", log.ModelId);
        Assert.Equal(4, log.RoleContext);
        Assert.Equal(900, log.InputTokens);
        Assert.Equal(100, log.OutputTokens);
        Assert.Equal(50, log.CacheReadInputTokens);
        Assert.Equal(20, log.CacheCreationInputTokens);
        Assert.Equal(4321, log.TotalDurationMs);

        // GnollBench usage (roles 4-8) is logged but consumes no chat quota.
        var stored = await db.SystemAiApiConfigurations.FindAsync(
            new object[] { config.Id }, TestContext.Current.CancellationToken);
        Assert.Equal(0, stored!.DailyChatTokensCount);
        Assert.Equal(0, stored.DailyChatRequestsCount);
    }

    [Fact]
    public async Task RecordUsageAsync_WithoutUserId_LeavesContextSavableForLaterChanges()
    {
        var (db, service) = CreateService();
        var config = await SeedConfigAsync(db);

        await service.RecordUsageAsync(config.Id, string.Empty, inputTokens: 10, outputTokens: 5, roleContext: 4);

        // The regression: a rejected usage row used to sit in the change tracker and fail
        // every later save made through the same context.
        config.Note = "still savable";
        int written = await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, written);
    }

    // --- Thinking level, cost and pricing source ---------------------------------------------

    /// <summary>A pricing service whose every configuration resolves to one fixed card.</summary>
    private sealed class FixedPricingService : ModelPricingService
    {
        private readonly ModelPricing? _card;

        public FixedPricingService(ModelPricing? card, ApplicationDbContext db)
            : base(new ModelMetadataService(), db)
        {
            _card = card;
        }

        public override ModelPricing? Resolve(SystemAiApiConfiguration config) => _card;
    }

    /// <summary>
    /// Haiku-5.5-shaped: $1 / $5 per million, cache read $0.10, cache write $1.25, and a
    /// long-context card at $2 / $10 above 100,000 prompt tokens of a single request.
    /// </summary>
    private static ModelPricing LongContextCard() => new(
        InputPerMillion: 1.00m,
        OutputPerMillion: 5.00m,
        CachedInputPerMillion: 0.10m,
        CacheWritePerMillion: 1.25m,
        Source: ModelPricingSource.Catalog,
        LongContext: new LongContextPricing(100_000, 2.00m, 10.00m));

    private static (ApplicationDbContext Db, SystemAiConfigService Service) CreatePricedService(ModelPricing? card)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        var db = new ApplicationDbContext(options);

        return (db, new SystemAiConfigService(db, NullLogger<SystemAiConfigService>.Instance,
            securityEvents: null, pricing: new FixedPricingService(card, db)));
    }

    private static TokenUsageReport Call(int prompt, int output) => new()
    {
        TotalPromptTokens = prompt,
        UncachedInputTokens = prompt,
        OutputTokens = output
    };

    [Fact]
    public async Task RecordUsageAsync_WithoutThinkingLevel_StoresTheConfigurationsLevel()
    {
        var (db, service) = CreateService();
        var config = await SeedConfigAsync(db);
        config.ThinkingLevel = "medium";
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await service.RecordUsageAsync(config.Id, TestUserId, inputTokens: 10, outputTokens: 5, roleContext: 4);

        Assert.Equal("medium", Assert.Single(db.SystemAiUsageLogs.ToList()).ThinkingLevel);
    }

    [Fact]
    public async Task RecordUsageAsync_WithAThinkingLevelOfNone_StoresAnEmptyString()
    {
        var (db, service) = CreateService();
        var config = await SeedConfigAsync(db);
        config.ThinkingLevel = "medium";
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await service.RecordUsageAsync(config.Id, TestUserId, inputTokens: 10, outputTokens: 5, roleContext: 1,
            thinkingLevel: new UsageThinkingLevel(null));

        Assert.Equal(string.Empty, Assert.Single(db.SystemAiUsageLogs.ToList()).ThinkingLevel);
    }

    [Fact]
    public async Task RecordUsageAsync_WithAnExplicitThinkingLevel_StoresIt()
    {
        var (db, service) = CreateService();
        var config = await SeedConfigAsync(db);
        config.ThinkingLevel = "medium";
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await service.RecordUsageAsync(config.Id, TestUserId, inputTokens: 10, outputTokens: 5, roleContext: 1,
            thinkingLevel: new UsageThinkingLevel("high"));

        Assert.Equal("high", Assert.Single(db.SystemAiUsageLogs.ToList()).ThinkingLevel);
    }

    [Fact]
    public async Task RecordUsageAsync_StoresTheCostAndItsSource()
    {
        var (db, service) = CreateService();
        var config = await SeedConfigAsync(db);

        await service.RecordUsageAsync(config.Id, TestUserId, inputTokens: 10, outputTokens: 5, roleContext: 1,
            estimatedCost: 0.0123m, pricingSource: "custom");

        var log = Assert.Single(db.SystemAiUsageLogs.ToList());
        Assert.Equal(0.0123m, log.EstimatedCost);
        Assert.Equal("custom", log.PricingSource);
    }

    [Fact]
    public async Task RecordUsageAsync_WithoutACost_StoresNoSource()
    {
        var (db, service) = CreateService();
        var config = await SeedConfigAsync(db);

        await service.RecordUsageAsync(config.Id, TestUserId, inputTokens: 10, outputTokens: 5, roleContext: 1,
            estimatedCost: null, pricingSource: "catalog");

        var log = Assert.Single(db.SystemAiUsageLogs.ToList());
        Assert.Null(log.EstimatedCost);
        Assert.Null(log.PricingSource);
    }

    [Fact]
    public async Task RecordCallsUsageAsync_SumsWholePromptTokens_AndPricesEachCallOnItsOwn()
    {
        var (db, service) = CreatePricedService(LongContextCard());
        var config = await SeedConfigAsync(db);

        // 120,000 prompt tokens in all, but neither request crosses the 100,000 threshold.
        await service.RecordCallsUsageAsync(config.Id, TestUserId, roleContext: 4,
            new List<TokenUsageReport> { Call(60_000, 1_000), Call(60_000, 1_000) },
            actualServiceTier: null, totalDurationMs: 2500);

        var log = Assert.Single(db.SystemAiUsageLogs.ToList());
        Assert.Equal(120_000, log.InputTokens);
        Assert.Equal(2_000, log.OutputTokens);
        Assert.Equal(0, log.CacheReadInputTokens);
        Assert.Equal(0, log.CacheCreationInputTokens);
        Assert.Equal(2500, log.TotalDurationMs);

        // Base rate per call: 60,000 x $1/M + 1,000 x $5/M = $0.065. The long-context card on the
        // summed tokens would have given 120,000 x $2/M + 2,000 x $10/M = $0.26.
        Assert.Equal(0.13m, log.EstimatedCost);
        Assert.Equal("catalog", log.PricingSource);
    }

    [Fact]
    public async Task RecordCallsUsageAsync_ModelCallRecords_SkipCallsWithoutUsage()
    {
        var (db, service) = CreatePricedService(LongContextCard());
        var config = await SeedConfigAsync(db);

        var calls = new List<Overseer.Services.Agents.ModelCallRecord>
        {
            new() { CallIndex = 0, Usage = Call(60_000, 1_000) },
            new() { CallIndex = 1, Usage = null }
        };
        await service.RecordCallsUsageAsync(config.Id, TestUserId, roleContext: 4, calls,
            actualServiceTier: null, totalDurationMs: null);

        var log = Assert.Single(db.SystemAiUsageLogs.ToList());
        Assert.Equal(60_000, log.InputTokens);
        Assert.Equal(1_000, log.OutputTokens);
        Assert.Equal(0.065m, log.EstimatedCost);
    }

    [Fact]
    public async Task RecordCallsUsageAsync_WithNoCalls_RecordsNothing()
    {
        var (db, service) = CreatePricedService(LongContextCard());
        var config = await SeedConfigAsync(db);

        await service.RecordCallsUsageAsync(config.Id, TestUserId, roleContext: 1,
            new List<TokenUsageReport>(), actualServiceTier: null, totalDurationMs: null);

        Assert.Empty(db.SystemAiUsageLogs);
        var stored = await db.SystemAiApiConfigurations.FindAsync(
            new object[] { config.Id }, TestContext.Current.CancellationToken);
        Assert.Equal(0, stored!.DailyChatRequestsCount);
    }

    [Fact]
    public async Task RecordCallsUsageAsync_WithoutAPricingService_StoresNoCostAndNoSource()
    {
        var (db, service) = CreateService();
        var config = await SeedConfigAsync(db);

        await service.RecordCallsUsageAsync(config.Id, TestUserId, roleContext: 4,
            new List<TokenUsageReport> { Call(1_000, 100) }, actualServiceTier: null, totalDurationMs: null);

        var log = Assert.Single(db.SystemAiUsageLogs.ToList());
        Assert.Equal(1_000, log.InputTokens);
        Assert.Null(log.EstimatedCost);
        Assert.Null(log.PricingSource);
    }
}
