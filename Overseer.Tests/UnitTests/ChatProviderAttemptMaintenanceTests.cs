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

/// <summary>
/// The prune-chat-provider-attempts maintenance action. Dry runs use the in-memory provider; the real
/// deletion is set-based and runs against SQLite in memory.
/// </summary>
public class ChatProviderAttemptMaintenanceTests
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

    private static ChatProviderAttempt Attempt(int daysAgo) => new()
    {
        StartedAtUtc = DateTime.UtcNow.AddDays(-daysAgo),
        AgentRunId = Guid.NewGuid(),
        RoleContext = 1,
        KeySource = AiKeySource.System,
        SystemAiApiConfigurationId = 1,
        Provider = "Anthropic",
        RequestedModelId = "claude-test",
        EndpointKind = BenchmarkEndpointKind.Official,
        Outcome = ProviderAttemptOutcome.Ok,
        HttpStatus = 200,
        DurationMs = 1200
    };

    private static async Task SeedAttemptsAsync(ApplicationDbContext db)
    {
        // The default window is 400 days.
        db.ChatProviderAttempts.AddRange(Attempt(500), Attempt(401), Attempt(10));
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static MaintenanceResultDto ResultOf(IActionResult actionResult)
    {
        var ok = Assert.IsType<OkObjectResult>(actionResult);
        return Assert.IsType<MaintenanceResultDto>(ok.Value);
    }

    [Fact]
    public async Task PruneChatProviderAttempts_DryRun_RecordsTheTriggerAndTheCountAndDeletesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = NewInMemoryDb();
        var (controller, retention) = CreateTestController(db);
        await SeedAttemptsAsync(db);

        var result = ResultOf(await controller.PruneChatProviderAttempts(new MaintenanceRequestDto { DryRun = true }, retention));

        Assert.True(result.Success);
        Assert.True(result.IsDryRun);
        Assert.Equal(MaintenanceTriggers.PruneChatProviderAttempts, result.Trigger);
        Assert.Equal("Manual:PruneChatProviderAttempts", result.Trigger);
        Assert.Equal(2, result.PrunedChatProviderAttemptCount);
        Assert.Contains("[DRY RUN] Pruned 2 chat provider attempts older than 400 days.", result.Logs);
        Assert.Equal(3, await db.ChatProviderAttempts.CountAsync(ct));

        var row = Assert.Single(await db.MaintenanceRunLogs.ToListAsync(ct));
        Assert.Equal("Manual:PruneChatProviderAttempts", row.Trigger);
        Assert.True(row.IsDryRun);
        Assert.True(row.Success);
        Assert.Equal(2, row.PrunedChatProviderAttemptCount);
        Assert.Equal(0, row.PrunedSecurityEventCount);
    }

    [Fact]
    public async Task PruneChatProviderAttempts_RequestWindow_OverridesTheConfiguredOne()
    {
        using var db = NewInMemoryDb();
        var (controller, retention) = CreateTestController(db);
        await SeedAttemptsAsync(db);

        var result = ResultOf(await controller.PruneChatProviderAttempts(
            new MaintenanceRequestDto { DryRun = true, ChatProviderAttemptPruneDays = 5 }, retention));

        Assert.Equal(3, result.PrunedChatProviderAttemptCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task PruneChatProviderAttempts_NonPositiveDays_IsBadRequestAndRecordsNothing(int days)
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = NewInMemoryDb();
        var (controller, retention) = CreateTestController(db);

        var actionResult = await controller.PruneChatProviderAttempts(
            new MaintenanceRequestDto { DryRun = true, ChatProviderAttemptPruneDays = days }, retention);

        var badRequest = Assert.IsType<BadRequestObjectResult>(actionResult);
        var message = badRequest.Value!.GetType().GetProperty("message")!.GetValue(badRequest.Value) as string;
        Assert.Equal("The chat provider attempt window must be a positive number of days.", message);
        Assert.Empty(await db.MaintenanceRunLogs.ToListAsync(ct));
    }

    [Fact]
    public async Task PruneChatProviderAttempts_RealRun_DeletesOnlyRowsOlderThanTheWindow()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, connection) = NewRelationalDb();
        using var _ = connection;
        using var __ = db;
        var (controller, retention) = CreateTestController(db);
        await SeedAttemptsAsync(db);

        var result = ResultOf(await controller.PruneChatProviderAttempts(new MaintenanceRequestDto { DryRun = false }, retention));

        Assert.True(result.Success);
        Assert.False(result.IsDryRun);
        Assert.Equal(2, result.PrunedChatProviderAttemptCount);
        Assert.Contains("Pruned 2 chat provider attempts older than 400 days.", result.Logs);

        var remaining = Assert.Single(await db.ChatProviderAttempts.AsNoTracking().ToListAsync(ct));
        Assert.True(remaining.StartedAtUtc > DateTime.UtcNow.AddDays(-400));

        var row = Assert.Single(await db.MaintenanceRunLogs.AsNoTracking().ToListAsync(ct));
        Assert.False(row.IsDryRun);
        Assert.Equal(2, row.PrunedChatProviderAttemptCount);
    }
}
