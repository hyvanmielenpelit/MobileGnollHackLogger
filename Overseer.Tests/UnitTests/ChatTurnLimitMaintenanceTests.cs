using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Controllers;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.Benchmarking;
using Overseer.Services.Providers;
using Overseer.Tests.Helpers;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>The prune-chat-turn-limit-events action, and what a purge does to an event's ids.</summary>
public class ChatTurnLimitMaintenanceTests
{
    private static ApplicationDbContext NewInMemoryDb()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static (ApplicationDbContext Db, Microsoft.Data.Sqlite.SqliteConnection Connection) NewRelationalDb()
    {
        var connection = SqliteSchemaTemplate.OpenCopy();
        var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection).Options);
        return (db, connection);
    }

    private static (AdminController controller, ChatRetentionService retention) CreateTestController(ApplicationDbContext db)
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
            new BenchmarkDifficultyJobManager(),
            new BenchmarkGenerationJobManager(),
            new BenchmarkRubricCheckJobManager(),
            new BenchmarkRubricGapAuthorJobManager(),
            new BenchmarkReportPackJobManager());
        var controller = new AdminController(db, config, null!, cryptoService, governor, endpointPolicy, usageGuard, pricingService);
        var retention = new ChatRetentionService(db, config, NullLogger<ChatRetentionService>.Instance);

        return (controller, retention);
    }

    private static ChatTurnLimitEvent Event(int daysAgo) => new()
    {
        OccurredUtc = DateTime.UtcNow.AddDays(-daysAgo),
        Code = "research_rounds",
        Provider = "Anthropic",
        RequestedModelId = "claude-test"
    };

    private static async Task SeedAsync(ApplicationDbContext db)
    {
        // The default window is 180 days.
        db.ChatTurnLimitEvents.AddRange(Event(400), Event(181), Event(10));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static MaintenanceResultDto ResultOf(IActionResult actionResult)
    {
        var ok = Assert.IsType<OkObjectResult>(actionResult);
        return Assert.IsType<MaintenanceResultDto>(ok.Value);
    }

    [Fact]
    public async Task DryRun_RecordsTheTriggerAndTheCountAndDeletesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = NewInMemoryDb();
        var (controller, retention) = CreateTestController(db);
        await SeedAsync(db);

        var result = ResultOf(await controller.PruneChatTurnLimitEvents(new MaintenanceRequestDto { DryRun = true }, retention));

        Assert.True(result.IsDryRun);
        Assert.Equal("Manual:PruneChatTurnLimitEvents", result.Trigger);
        Assert.Equal(2, result.PrunedChatTurnLimitEventCount);
        Assert.Equal(3, await db.ChatTurnLimitEvents.CountAsync(ct));

        var row = Assert.Single(await db.MaintenanceRunLogs.ToListAsync(ct));
        Assert.Equal(2, row.PrunedChatTurnLimitEventCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task NonPositiveDays_IsBadRequest(int days)
    {
        using var db = NewInMemoryDb();
        var (controller, retention) = CreateTestController(db);

        var actionResult = await controller.PruneChatTurnLimitEvents(
            new MaintenanceRequestDto { DryRun = true, ChatTurnLimitEventPruneDays = days }, retention);

        Assert.IsType<BadRequestObjectResult>(actionResult);
    }

    [Fact]
    public async Task RealRun_DeletesOnlyRowsOlderThanTheWindow()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, connection) = NewRelationalDb();
        using var _ = connection;
        using var __ = db;
        var (controller, retention) = CreateTestController(db);
        await SeedAsync(db);

        var result = ResultOf(await controller.PruneChatTurnLimitEvents(new MaintenanceRequestDto { DryRun = false }, retention));

        Assert.Equal(2, result.PrunedChatTurnLimitEventCount);
        Assert.Single(await db.ChatTurnLimitEvents.AsNoTracking().ToListAsync(ct));
    }

    [Fact]
    public async Task Purge_ClearsTheEventsIds_AndKeepsTheEvent()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, connection) = NewRelationalDb();
        using var _ = connection;
        using var __ = db;
        var (_, retention) = CreateTestController(db);

        db.Users.Add(new ApplicationUser { Id = "purge-user", UserName = "purge-user" });
        var session = new ChatSession { AspNetUserId = "purge-user", Title = "t", CreatedUtc = DateTime.UtcNow, LastMessageUtc = DateTime.UtcNow };
        db.ChatSession.Add(session);
        await db.SaveChangesAsync(ct);

        var other = Event(1);
        other.ChatSessionId = session.Id + 1000;
        var limitEvent = Event(1);
        limitEvent.AspNetUserId = "purge-user";
        limitEvent.ChatSessionId = session.Id;
        limitEvent.UserMessageId = 5;
        limitEvent.AssistantMessageId = 6;
        db.ChatTurnLimitEvents.AddRange(limitEvent, other);
        await db.SaveChangesAsync(ct);

        await retention.PermanentlyPurgeSessionsAsync(new List<long> { session.Id }, cancellationToken: ct);

        var rows = await db.ChatTurnLimitEvents.AsNoTracking().OrderBy(e => e.Id).ToListAsync(ct);
        Assert.Equal(2, rows.Count);
        Assert.Null(rows[0].ChatSessionId);
        Assert.Null(rows[0].UserMessageId);
        Assert.Null(rows[0].AssistantMessageId);
        Assert.NotNull(rows[1].ChatSessionId);
    }

    [Fact]
    public void Default_IsOneHundredEightyDays()
    {
        Assert.Equal(180, new ChatRetentionSettings().PruneChatTurnLimitEventDays);
    }
}
