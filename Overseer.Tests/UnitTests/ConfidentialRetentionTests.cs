using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Services;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// All four soft-delete paths, one test each.
/// </summary>
/// <remarks>
/// A guarantee that holds for one deletion gesture and not the other three is not a guarantee.
/// Two of these paths fire with no user gesture at all — quota eviction runs on ordinary
/// session creation and on every snapshot attach, and inactivity expiry runs nightly — so those
/// are where a confidential chat could quietly enter a trash it was promised it would not.
/// </remarks>
public class ConfidentialRetentionTests : IDisposable
{
    private readonly string _dataDir =
        Path.Combine(Path.GetTempPath(), "OverseerConfidentialRetentionTests_" + Guid.NewGuid());

    private ApplicationDbContext CreateDb()
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private IConfiguration CreateConfig(int maxActiveSessions = 50) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "ChatRetentionSettings:MaxActiveSessionsPerUser", maxActiveSessions.ToString() },
            { "ChatRetentionSettings:MaxPinnedSessionsPerUser", "5" },
            { "ChatRetentionSettings:InactivityTtlDays", "90" },
            { "ChatRetentionSettings:SoftDeleteGracePeriodDays", "30" },
            { "ConversationsDataLocation", _dataDir }
        }).Build();

    /* Records which sessions a deletion path routed to the purge, and removes their rows so
       the outcome is observable, instead of running the bulk-SQL statements the in-memory
       provider does not support. What each of the four paths has to get right is the
       partitioning decision; the SQL beneath the purge is unchanged by this stage. */
    private sealed class RecordingRetentionService : ChatRetentionService
    {
        private readonly ApplicationDbContext _db;

        public RecordingRetentionService(ApplicationDbContext db, IConfiguration config)
            : base(db, config, NullLogger<ChatRetentionService>.Instance)
            => _db = db;

        public List<long> PurgedIds { get; } = new();

        public override async Task<Overseer.Models.MaintenanceResultDto> PermanentlyPurgeSessionsAsync(
            List<long> sessionIds, bool isDryRun = false, CancellationToken cancellationToken = default)
        {
            PurgedIds.AddRange(sessionIds);

            if (!isDryRun)
            {
                var rows = await _db.ChatSession.Where(s => sessionIds.Contains(s.Id)).ToListAsync(cancellationToken);
                _db.ChatSession.RemoveRange(rows);
                await _db.SaveChangesAsync(cancellationToken);
            }

            return new Overseer.Models.MaintenanceResultDto { Success = true, IsDryRun = isDryRun };
        }
    }

    private RecordingRetentionService CreateService(ApplicationDbContext db, IConfiguration? config = null)
        => new(db, config ?? CreateConfig());

    /// <param name="immediatePurge">Whether the session carries the materialised purge flag.</param>
    private static ChatSession Session(
        long id, string userId, bool immediatePurge, DateTime? lastMessage = null, int? retentionDays = null)
        => new()
        {
            Id = id,
            AspNetUserId = userId,
            Title = immediatePurge ? "Confidential chat" : "Normal chat",
            CreatedUtc = DateTime.UtcNow.AddDays(-100),
            LastMessageUtc = lastMessage ?? DateTime.UtcNow,
            IsConfidential = immediatePurge,
            ImmediatePurgeOnDelete = immediatePurge,
            EffectiveRetentionDays = retentionDays
        };

    // ── Path 1 of 4: explicit single-session delete ─────────────────────────────

    [Fact]
    public async Task SoftDeleteSessionAsync_PurgesAnImmediatePurgeSessionAndTrashesANormalOne()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateDb();
        db.ChatSession.AddRange(Session(1, "u1", immediatePurge: true), Session(2, "u1", immediatePurge: false));
        await db.SaveChangesAsync(ct);

        var service = CreateService(db);

        Assert.True(await service.SoftDeleteSessionAsync(1, "u1", "User", ct));
        Assert.True(await service.SoftDeleteSessionAsync(2, "u1", "User", ct));

        // Purged: no row at all, so no trash entry and no grace period.
        Assert.Null(await db.ChatSession.FindAsync([1L], ct));

        var trashed = await db.ChatSession.FindAsync([2L], ct);
        Assert.NotNull(trashed);
        Assert.True(trashed!.IsDeleted);
    }

    // ── Path 2 of 4: bulk delete ────────────────────────────────────────────────

    [Fact]
    public async Task BulkSoftDeleteSessionsAsync_PurgesTheConfidentialOnesAndTrashesTheRest()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateDb();
        db.ChatSession.AddRange(
            Session(1, "u1", immediatePurge: true),
            Session(2, "u1", immediatePurge: false),
            Session(3, "u1", immediatePurge: true));
        await db.SaveChangesAsync(ct);

        var service = CreateService(db);
        int count = await service.BulkSoftDeleteSessionsAsync("u1", includePinned: false, "User", ct);

        Assert.Equal(3, count);
        Assert.Empty(await db.ChatSession.Where(s => s.Id == 1 || s.Id == 3).ToListAsync(ct));
        Assert.True((await db.ChatSession.FindAsync([2L], ct))!.IsDeleted);
    }

    // ── Path 3 of 4: quota eviction, which fires with no user gesture ───────────

    [Fact]
    public async Task EnforceUserSessionQuotaAsync_PurgesAnEvictedConfidentialSessionRatherThanTrashingIt()
    {
        /* The sharpest of the four. Quota eviction runs on ordinary session creation and on
           every snapshot attach, so this is a deletion nobody gestured at — and v3 of the plan
           missed it entirely. */
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateDb();

        // Two sessions, a quota of one: the older is evicted.
        db.ChatSession.AddRange(
            Session(1, "u1", immediatePurge: true, lastMessage: DateTime.UtcNow.AddDays(-5)),
            Session(2, "u1", immediatePurge: false, lastMessage: DateTime.UtcNow));
        await db.SaveChangesAsync(ct);

        var service = CreateService(db, CreateConfig(maxActiveSessions: 1));
        int evicted = await service.EnforceUserSessionQuotaAsync("u1", ct);

        Assert.Equal(1, evicted);
        Assert.Null(await db.ChatSession.FindAsync([1L], ct));
        Assert.False((await db.ChatSession.FindAsync([2L], ct))!.IsDeleted);
    }

    [Fact]
    public async Task EnforceUserSessionQuotaAsync_StillTrashesAnEvictedNormalSession()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateDb();
        db.ChatSession.AddRange(
            Session(1, "u1", immediatePurge: false, lastMessage: DateTime.UtcNow.AddDays(-5)),
            Session(2, "u1", immediatePurge: false, lastMessage: DateTime.UtcNow));
        await db.SaveChangesAsync(ct);

        var service = CreateService(db, CreateConfig(maxActiveSessions: 1));
        await service.EnforceUserSessionQuotaAsync("u1", ct);

        var evicted = await db.ChatSession.FindAsync([1L], ct);
        Assert.NotNull(evicted);
        Assert.True(evicted!.IsDeleted);
        Assert.Equal("Quota", evicted.DeletionReason);
    }

    // ── Path 4 of 4: inactivity expiry ──────────────────────────────────────────

    [Fact]
    public async Task SoftDeleteInactiveSessionsAsync_ExpiresASessionPastItsOwnTtlAndLeavesNoTrashRow()
    {
        /* The 30-versus-60-day regression. The grace period is 30 days, so a confidential
           session with a 30-day TTL that soft-deleted on expiry would be retained for 60 —
           under a 30-day promise. */
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateDb();

        db.ChatSession.Add(Session(
            1, "u1", immediatePurge: true,
            lastMessage: DateTime.UtcNow.AddDays(-31), retentionDays: 30));

        // Not yet past its own TTL, and well inside the global 90.
        db.ChatSession.Add(Session(
            2, "u1", immediatePurge: true,
            lastMessage: DateTime.UtcNow.AddDays(-10), retentionDays: 30));
        await db.SaveChangesAsync(ct);

        var service = CreateService(db);
        int count = await service.SoftDeleteInactiveSessionsAsync(inactivityDays: 90, isDryRun: false, ct);

        Assert.Equal(1, count);
        Assert.Null(await db.ChatSession.FindAsync([1L], ct));
        Assert.False((await db.ChatSession.FindAsync([2L], ct))!.IsDeleted);
    }

    [Fact]
    public async Task SoftDeleteInactiveSessionsAsync_StillAppliesTheGlobalTtlToSessionsWithoutTheirOwn()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateDb();

        // No materialised TTL, so the global 90-day sweep governs — the original behaviour.
        db.ChatSession.Add(Session(1, "u1", immediatePurge: false, lastMessage: DateTime.UtcNow.AddDays(-100)));
        db.ChatSession.Add(Session(2, "u1", immediatePurge: false, lastMessage: DateTime.UtcNow.AddDays(-10)));
        await db.SaveChangesAsync(ct);

        /* A dry run, because the global pass ends in an ExecuteUpdateAsync the in-memory
           provider cannot run. What this stage changed there is the added
           EffectiveRetentionDays == null filter, and the count is exactly what proves it: the
           100-day-old session is selected and the 10-day-old one is not. */
        var service = CreateService(db);
        int count = await service.SoftDeleteInactiveSessionsAsync(inactivityDays: 90, isDryRun: true, ct);

        Assert.Equal(1, count);
        Assert.Empty(service.PurgedIds);
    }

    [Fact]
    public async Task SoftDeleteInactiveSessionsAsync_AConfidentialSessionWithoutPurgeStillGoesToTheTrash()
    {
        /* Immediate purge is user-adjustable, so a confidential session can legitimately have
           it off. Expiry then behaves like any other session's — which is what makes the badge
           report the mode as not keeping its full promise. */
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateDb();

        var session = Session(1, "u1", immediatePurge: false, lastMessage: DateTime.UtcNow.AddDays(-31), retentionDays: 30);
        session.IsConfidential = true;
        db.ChatSession.Add(session);
        await db.SaveChangesAsync(ct);

        var service = CreateService(db);
        await service.SoftDeleteInactiveSessionsAsync(inactivityDays: 90, isDryRun: false, ct);

        var expired = await db.ChatSession.FindAsync([1L], ct);
        Assert.NotNull(expired);
        Assert.True(expired!.IsDeleted);
    }

    [Fact]
    public async Task SoftDeleteInactiveSessionsAsync_DryRunChangesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateDb();
        db.ChatSession.Add(Session(1, "u1", immediatePurge: true, lastMessage: DateTime.UtcNow.AddDays(-31), retentionDays: 30));
        await db.SaveChangesAsync(ct);

        var service = CreateService(db);
        int count = await service.SoftDeleteInactiveSessionsAsync(inactivityDays: 90, isDryRun: true, ct);

        Assert.Equal(1, count);
        Assert.NotNull(await db.ChatSession.FindAsync([1L], ct));
        Assert.False((await db.ChatSession.FindAsync([1L], ct))!.IsDeleted);
    }

    [Fact]
    public async Task ExpirySweep_HandlesSeveralDistinctTtlsInOnePass()
    {
        // One query per distinct value, not one per session.
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateDb();

        db.ChatSession.Add(Session(1, "u1", true, DateTime.UtcNow.AddDays(-8), retentionDays: 7));
        db.ChatSession.Add(Session(2, "u2", true, DateTime.UtcNow.AddDays(-31), retentionDays: 30));
        db.ChatSession.Add(Session(3, "u3", true, DateTime.UtcNow.AddDays(-5), retentionDays: 7));
        await db.SaveChangesAsync(ct);

        var service = CreateService(db);
        int count = await service.SoftDeleteInactiveSessionsAsync(inactivityDays: 90, isDryRun: false, ct);

        Assert.Equal(2, count);
        Assert.Null(await db.ChatSession.FindAsync([1L], ct));
        Assert.Null(await db.ChatSession.FindAsync([2L], ct));
        Assert.NotNull(await db.ChatSession.FindAsync([3L], ct));
    }

    [Fact]
    public async Task ExpirySweep_NeverTouchesAPinnedSession()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateDb();

        var pinned = Session(1, "u1", true, DateTime.UtcNow.AddDays(-400), retentionDays: 30);
        pinned.IsPinned = true;
        db.ChatSession.Add(pinned);
        await db.SaveChangesAsync(ct);

        var service = CreateService(db);
        int count = await service.SoftDeleteInactiveSessionsAsync(inactivityDays: 90, isDryRun: false, ct);

        Assert.Equal(0, count);
        Assert.NotNull(await db.ChatSession.FindAsync([1L], ct));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dataDir))
                Directory.Delete(_dataDir, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a run over.
        }
    }
}
