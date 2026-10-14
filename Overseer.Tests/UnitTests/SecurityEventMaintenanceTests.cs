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
/// The prune-security-events maintenance action, against SQLite in memory: the held-user exclusion is a
/// subquery and the deletion is set-based, and both need a relational provider to be exercised as written.
/// </summary>
public class SecurityEventMaintenanceTests
{
    private const string HeldUser = "held-user";
    private const string ReleasedUser = "released-user";
    private const string PendingUser = "pending-user";
    private const string DeclinedUser = "declined-user";
    private const string OtherUser = "other-user";
    private const string Admin = "admin-user";

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

    private static SecurityEvent Event(string? userId, int lastSeenDaysAgo) => new()
    {
        OccurredUtc = DateTime.UtcNow.AddDays(-lastSeenDaysAgo - 1),
        LastOccurredUtc = DateTime.UtcNow.AddDays(-lastSeenDaysAgo),
        Kind = SecurityEventKind.ProviderSafetyBlock,
        Count = 1,
        AspNetUserId = userId,
        Code = "SAFETY"
    };

    private static async Task SeedUsersAsync(ApplicationDbContext db, params string[] userIds)
    {
        foreach (var id in userIds)
        {
            db.Users.Add(new ApplicationUser { Id = id, UserName = id });
        }
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static AccountHold Hold(string userId, bool released) => new()
    {
        AspNetUserId = userId,
        Reason = "Under review",
        PlacedByUserId = Admin,
        PlacedUtc = DateTime.UtcNow.AddDays(-300),
        ReleasedByUserId = released ? Admin : null,
        ReleasedUtc = released ? DateTime.UtcNow.AddDays(-250) : null
    };

    private static AccountDeletionRequest DeletionRequest(string userId, AccountDeletionRequestStatus status) => new()
    {
        AspNetUserId = userId,
        UserNameSnapshot = userId,
        RequestedUtc = DateTime.UtcNow.AddDays(-5),
        Status = status,
        HoldReasons = "Automatic hold",
        DecidedUtc = status == AccountDeletionRequestStatus.Pending ? null : DateTime.UtcNow.AddDays(-1)
    };

    private static MaintenanceResultDto ResultOf(IActionResult actionResult)
    {
        var ok = Assert.IsType<OkObjectResult>(actionResult);
        return Assert.IsType<MaintenanceResultDto>(ok.Value);
    }

    [Fact]
    public async Task PruneSecurityEvents_DryRun_RecordsTheTriggerAndTheCountAndDeletesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, connection) = NewRelationalDb();
        using var _ = connection;
        using var __ = db;
        var (controller, retention) = CreateTestController(db);

        // The default window is 180 days since the run was last seen.
        db.SecurityEvents.AddRange(Event(null, 400), Event(null, 181), Event(null, 10));
        await db.SaveChangesAsync(ct);

        var result = ResultOf(await controller.PruneSecurityEvents(new MaintenanceRequestDto { DryRun = true }, retention));

        Assert.True(result.Success);
        Assert.True(result.IsDryRun);
        Assert.Equal(MaintenanceTriggers.PruneSecurityEvents, result.Trigger);
        Assert.Equal("Manual:PruneSecurityEvents", result.Trigger);
        Assert.Equal(2, result.PrunedSecurityEventCount);
        Assert.Contains(result.Logs, l => l.StartsWith("[DRY RUN] Pruned 2 security events last seen over 180 days ago.", StringComparison.Ordinal));
        Assert.Equal(3, await db.SecurityEvents.CountAsync(ct));

        var row = Assert.Single(await db.MaintenanceRunLogs.AsNoTracking().ToListAsync(ct));
        Assert.Equal("Manual:PruneSecurityEvents", row.Trigger);
        Assert.True(row.IsDryRun);
        Assert.True(row.Success);
        Assert.Equal(2, row.PrunedSecurityEventCount);
        Assert.Equal(0, row.PrunedChatProviderAttemptCount);
    }

    [Fact]
    public async Task PruneSecurityEvents_RequestWindow_OverridesTheConfiguredOne()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, connection) = NewRelationalDb();
        using var _ = connection;
        using var __ = db;
        var (controller, retention) = CreateTestController(db);

        db.SecurityEvents.AddRange(Event(null, 400), Event(null, 181), Event(null, 10));
        await db.SaveChangesAsync(ct);

        var result = ResultOf(await controller.PruneSecurityEvents(
            new MaintenanceRequestDto { DryRun = true, SecurityEventPruneDays = 5 }, retention));

        Assert.Equal(3, result.PrunedSecurityEventCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task PruneSecurityEvents_NonPositiveDays_IsBadRequestAndRecordsNothing(int days)
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, connection) = NewRelationalDb();
        using var _ = connection;
        using var __ = db;
        var (controller, retention) = CreateTestController(db);

        var actionResult = await controller.PruneSecurityEvents(
            new MaintenanceRequestDto { DryRun = true, SecurityEventPruneDays = days }, retention);

        var badRequest = Assert.IsType<BadRequestObjectResult>(actionResult);
        var message = badRequest.Value!.GetType().GetProperty("message")!.GetValue(badRequest.Value) as string;
        Assert.Equal("The security event window must be a positive number of days.", message);
        Assert.Empty(await db.MaintenanceRunLogs.ToListAsync(ct));
    }

    [Fact]
    public async Task PruneSecurityEvents_DryRun_DoesNotCountTheEventsOfAUserWithAnActiveHold()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, connection) = NewRelationalDb();
        using var _ = connection;
        using var __ = db;
        var (controller, retention) = CreateTestController(db);
        await SeedUsersAsync(db, HeldUser, ReleasedUser, OtherUser);

        db.AccountHolds.Add(Hold(HeldUser, released: false));
        db.AccountHolds.Add(Hold(ReleasedUser, released: true));
        db.SecurityEvents.AddRange(
            Event(HeldUser, 400),
            Event(ReleasedUser, 400),
            Event(OtherUser, 400),
            Event(null, 400));
        await db.SaveChangesAsync(ct);

        var result = ResultOf(await controller.PruneSecurityEvents(new MaintenanceRequestDto { DryRun = true }, retention));

        // The released hold no longer protects its user; events with no user are never held.
        Assert.Equal(3, result.PrunedSecurityEventCount);
        Assert.Equal(0, await ChatRetentionService.PrunableSecurityEvents(db, DateTime.UtcNow.AddDays(-180))
            .CountAsync(e => e.AspNetUserId == HeldUser, ct));
    }

    [Fact]
    public async Task PruneSecurityEvents_DryRun_DoesNotCountTheEventsOfAUserWithAPendingDeletionRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, connection) = NewRelationalDb();
        using var _ = connection;
        using var __ = db;
        var (controller, retention) = CreateTestController(db);
        await SeedUsersAsync(db, PendingUser, DeclinedUser, OtherUser);

        db.AccountDeletionRequests.Add(DeletionRequest(PendingUser, AccountDeletionRequestStatus.Pending));
        db.AccountDeletionRequests.Add(DeletionRequest(DeclinedUser, AccountDeletionRequestStatus.Declined));
        db.SecurityEvents.AddRange(
            Event(PendingUser, 400),
            Event(DeclinedUser, 400),
            Event(OtherUser, 400));
        await db.SaveChangesAsync(ct);

        var result = ResultOf(await controller.PruneSecurityEvents(new MaintenanceRequestDto { DryRun = true }, retention));

        // A decided request no longer protects its user.
        Assert.Equal(2, result.PrunedSecurityEventCount);
        Assert.Equal(0, await ChatRetentionService.PrunableSecurityEvents(db, DateTime.UtcNow.AddDays(-180))
            .CountAsync(e => e.AspNetUserId == PendingUser, ct));
    }

    [Fact]
    public async Task PruneSecurityEvents_RealRun_DeletesOnlyOldEventsOfUnheldUsers()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, connection) = NewRelationalDb();
        using var _ = connection;
        using var __ = db;
        var (controller, retention) = CreateTestController(db);
        await SeedUsersAsync(db, HeldUser, PendingUser, OtherUser);

        db.AccountHolds.Add(Hold(HeldUser, released: false));
        db.AccountDeletionRequests.Add(DeletionRequest(PendingUser, AccountDeletionRequestStatus.Pending));
        db.SecurityEvents.AddRange(
            Event(HeldUser, 400),
            Event(PendingUser, 400),
            Event(OtherUser, 400),
            Event(OtherUser, 10),
            Event(null, 400));
        await db.SaveChangesAsync(ct);

        var result = ResultOf(await controller.PruneSecurityEvents(new MaintenanceRequestDto { DryRun = false }, retention));

        Assert.True(result.Success);
        Assert.False(result.IsDryRun);
        Assert.Equal(2, result.PrunedSecurityEventCount);

        var remaining = await db.SecurityEvents.AsNoTracking().OrderBy(e => e.Id).Select(e => e.AspNetUserId).ToListAsync(ct);
        Assert.Equal(new string?[] { HeldUser, PendingUser, OtherUser }, remaining);

        var row = Assert.Single(await db.MaintenanceRunLogs.AsNoTracking().ToListAsync(ct));
        Assert.False(row.IsDryRun);
        Assert.Equal(2, row.PrunedSecurityEventCount);
    }
}
