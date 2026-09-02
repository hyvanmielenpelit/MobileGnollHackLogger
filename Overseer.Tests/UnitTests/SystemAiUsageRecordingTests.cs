using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Services;
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

        await service.RecordUsageAsync(config.Id, userId, inputTokens: 1200, outputTokens: 300, roleContext: 4);

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

        var stored = await db.SystemAiApiConfigurations.FindAsync(
            new object[] { config.Id }, TestContext.Current.CancellationToken);
        Assert.Equal(1000, stored!.DailyChatTokensCount);
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
}
