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

    /// <summary>A quarantine row of <paramref name="securityEvent"/>; the sealed columns are placeholders the prune never reads.</summary>
    private static SnapshotGuardQuarantine Quarantine(SecurityEvent securityEvent, int daysAgo) => new()
    {
        SecurityEvent = securityEvent,
        OccurredUtc = DateTime.UtcNow.AddDays(-daysAgo),
        AspNetUserId = securityEvent.AspNetUserId,
        Channel = "create",
        Subject = "Board",
        TextKind = "Snapshot",
        Level = "Injection",
        Codes = "authority",
        SessionMode = "normal",
        Sha256 = new string('0', 64),
        KeyVersion = "v1",
        Ciphertext = "placeholder",
        Nonce = "placeholder",
        Tag = "placeholder"
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
        Assert.Contains(result.Logs, l => l.StartsWith("[DRY RUN] Pruned 2 security events last seen over 180 days ago and 0 quarantined game texts older than 30 days.", StringComparison.Ordinal));
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

    [Fact]
    public async Task PruneSnapshotQuarantine_DeletesRowsByTheirOwnAge_IncludingThoseOfHeldUsers()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, connection) = NewRelationalDb();
        using var _ = connection;
        using var __ = db;
        var retention = CreateTestController(db).retention;
        await SeedUsersAsync(db, HeldUser, OtherUser);

        db.AccountHolds.Add(Hold(HeldUser, released: false));
        // Recent events: only the texts' own age decides.
        var held = Event(HeldUser, 1);
        var other = Event(OtherUser, 1);
        db.SecurityEvents.AddRange(held, other);
        db.SnapshotGuardQuarantines.AddRange(
            Quarantine(held, 40),
            Quarantine(other, 31),
            Quarantine(other, 5));
        await db.SaveChangesAsync(ct);

        Assert.Equal(2, await retention.PruneSnapshotQuarantineAsync(30, isDryRun: true, ct));
        Assert.Equal(3, await db.SnapshotGuardQuarantines.CountAsync(ct));

        Assert.Equal(2, await retention.PruneSnapshotQuarantineAsync(30, isDryRun: false, ct));
        var remaining = Assert.Single(await db.SnapshotGuardQuarantines.AsNoTracking().ToListAsync(ct));
        Assert.Equal(other.Id, remaining.SecurityEventId);
        Assert.Equal(2, await db.SecurityEvents.CountAsync(ct));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task PruneSnapshotQuarantine_NonPositiveDays_IsDisabled(int days)
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, connection) = NewRelationalDb();
        using var _ = connection;
        using var __ = db;
        var retention = CreateTestController(db).retention;

        var securityEvent = Event(null, 1);
        db.SecurityEvents.Add(securityEvent);
        db.SnapshotGuardQuarantines.Add(Quarantine(securityEvent, 400));
        await db.SaveChangesAsync(ct);

        Assert.Equal(0, await retention.PruneSnapshotQuarantineAsync(days, isDryRun: false, ct));
        Assert.Equal(1, await db.SnapshotGuardQuarantines.CountAsync(ct));
    }

    [Fact]
    public async Task PruneSecurityEvents_AlsoPrunesTheQuarantine_AndSaysSoInItsLogLine()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, connection) = NewRelationalDb();
        using var _ = connection;
        using var __ = db;
        var (controller, retention) = CreateTestController(db);

        var recent = Event(null, 1);
        db.SecurityEvents.AddRange(recent, Event(null, 400));
        db.SnapshotGuardQuarantines.AddRange(Quarantine(recent, 40), Quarantine(recent, 2));
        await db.SaveChangesAsync(ct);

        var result = ResultOf(await controller.PruneSecurityEvents(new MaintenanceRequestDto { DryRun = false }, retention));

        Assert.Equal(1, result.PrunedSecurityEventCount);
        Assert.Contains("Pruned 1 security events last seen over 180 days ago and 1 quarantined game texts older than 30 days. Events of users with an active account hold or a pending deletion request are kept.", result.Logs);
        Assert.Equal(1, await db.SnapshotGuardQuarantines.CountAsync(ct));
    }

    [Fact]
    public async Task PruneSecurityEvents_WithTheQuarantinePruneDisabled_SaysSo()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, connection) = NewRelationalDb();
        using var _ = connection;
        using var __ = db;
        var controller = CreateTestController(db).controller;
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { { "ChatRetentionSettings:PruneSnapshotQuarantineDays", "0" } })
            .Build();
        var retention = new ChatRetentionService(db, config, NullLogger<ChatRetentionService>.Instance);

        var securityEvent = Event(null, 1);
        db.SecurityEvents.Add(securityEvent);
        db.SnapshotGuardQuarantines.Add(Quarantine(securityEvent, 400));
        await db.SaveChangesAsync(ct);

        var result = ResultOf(await controller.PruneSecurityEvents(new MaintenanceRequestDto { DryRun = true }, retention));

        Assert.Contains("[DRY RUN] Pruned 0 security events last seen over 180 days ago; quarantined game text pruning is disabled. Events of users with an active account hold or a pending deletion request are kept.", result.Logs);
        Assert.Equal(1, await db.SnapshotGuardQuarantines.CountAsync(ct));
    }

    [Fact]
    public async Task PruningAnEvent_DeletesItsQuarantineRows()
    {
        var ct = TestContext.Current.CancellationToken;
        var (db, connection) = NewRelationalDb();
        using var _ = connection;
        using var __ = db;
        var retention = CreateTestController(db).retention;

        var old = Event(null, 400);
        var recent = Event(null, 1);
        db.SecurityEvents.AddRange(old, recent);
        db.SnapshotGuardQuarantines.AddRange(Quarantine(old, 2), Quarantine(old, 3), Quarantine(recent, 2));
        await db.SaveChangesAsync(ct);

        Assert.Equal(1, await retention.PruneSecurityEventsAsync(180, isDryRun: false, ct));

        var remaining = Assert.Single(await db.SnapshotGuardQuarantines.AsNoTracking().ToListAsync(ct));
        Assert.Equal(recent.Id, remaining.SecurityEventId);
    }
}
