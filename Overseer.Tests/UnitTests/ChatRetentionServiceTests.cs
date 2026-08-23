using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Services;
using Xunit;

namespace Overseer.Tests.UnitTests;

public class ChatRetentionServiceTests
{
    private ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private IConfiguration CreateTestConfiguration(Dictionary<string, string?>? inMemorySettings = null)
    {
        var settings = new Dictionary<string, string?>
        {
            { "ChatRetentionSettings:MaxActiveSessionsPerUser", "3" },
            { "ChatRetentionSettings:MaxPinnedSessionsPerUser", "2" },
            { "ChatRetentionSettings:InactivityTtlDays", "90" },
            { "ChatRetentionSettings:SoftDeleteGracePeriodDays", "30" },
            { "ChatRetentionSettings:PruneToolCallResultsDays", "30" },
            { "ConversationsDataLocation", Path.Combine(Path.GetTempPath(), "OverseerTestConversations_" + Guid.NewGuid()) }
        };

        if (inMemorySettings != null)
        {
            foreach (var kv in inMemorySettings)
            {
                settings[kv.Key] = kv.Value;
            }
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
    }

    [Fact]
    public async Task EnforceUserSessionQuota_SoftDeletesOldestUnpinned_WhenOverQuota()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateInMemoryDbContext();
        var config = CreateTestConfiguration();
        var service = new ChatRetentionService(db, config, NullLogger<ChatRetentionService>.Instance);
        var userId = "user1";

        // Create 5 sessions with different LastMessageUtc
        for (int i = 1; i <= 5; i++)
        {
            db.ChatSession.Add(new ChatSession
            {
                Id = i,
                AspNetUserId = userId,
                Title = $"Chat {i}",
                CreatedUtc = DateTime.UtcNow.AddDays(-10 + i),
                LastMessageUtc = DateTime.UtcNow.AddDays(-10 + i),
                IsDeleted = false,
                IsPinned = false
            });
        }
        await db.SaveChangesAsync(ct);

        // Enforce quota (limit is 3) -> should soft delete 2 oldest (Chat 1 and Chat 2)
        int softDeleted = await service.EnforceUserSessionQuotaAsync(userId, ct);

        Assert.Equal(2, softDeleted);

        var activeSessions = await db.ChatSession.Where(s => s.AspNetUserId == userId && !s.IsDeleted).ToListAsync(ct);
        Assert.Equal(3, activeSessions.Count);

        var deletedSessions = await db.ChatSession.Where(s => s.AspNetUserId == userId && s.IsDeleted).ToListAsync(ct);
        Assert.Equal(2, deletedSessions.Count);
        Assert.All(deletedSessions, s => Assert.Equal("Quota", s.DeletionReason));
    }

    [Fact]
    public async Task EnforceUserSessionQuota_PreservesPinnedSessions()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateInMemoryDbContext();
        var config = CreateTestConfiguration(); // MaxActiveSessionsPerUser = 3
        var service = new ChatRetentionService(db, config, NullLogger<ChatRetentionService>.Instance);
        var userId = "user1";

        // Create 5 sessions, oldest is pinned
        db.ChatSession.Add(new ChatSession { Id = 1, AspNetUserId = userId, Title = "Pinned Old Chat", LastMessageUtc = DateTime.UtcNow.AddDays(-20), IsPinned = true });
        db.ChatSession.Add(new ChatSession { Id = 2, AspNetUserId = userId, Title = "Unpinned 1", LastMessageUtc = DateTime.UtcNow.AddDays(-10), IsPinned = false });
        db.ChatSession.Add(new ChatSession { Id = 3, AspNetUserId = userId, Title = "Unpinned 2", LastMessageUtc = DateTime.UtcNow.AddDays(-5), IsPinned = false });
        db.ChatSession.Add(new ChatSession { Id = 4, AspNetUserId = userId, Title = "Unpinned 3", LastMessageUtc = DateTime.UtcNow.AddDays(-2), IsPinned = false });
        db.ChatSession.Add(new ChatSession { Id = 5, AspNetUserId = userId, Title = "Unpinned 4", LastMessageUtc = DateTime.UtcNow, IsPinned = false });
        await db.SaveChangesAsync(ct);

        // 5 total active sessions (quota is 3) -> 2 oldest unpinned (Chat 2 and Chat 3) are soft-deleted
        int softDeleted = await service.EnforceUserSessionQuotaAsync(userId, ct);

        Assert.Equal(2, softDeleted);

        var pinned = await db.ChatSession.FindAsync(new object[] { (long)1 }, ct);
        Assert.NotNull(pinned);
        Assert.False(pinned.IsDeleted, "Pinned session must NOT be soft-deleted by quota enforcement.");

        var deleted1 = await db.ChatSession.FindAsync(new object[] { (long)2 }, ct);
        Assert.NotNull(deleted1);
        Assert.True(deleted1.IsDeleted);

        var deleted2 = await db.ChatSession.FindAsync(new object[] { (long)3 }, ct);
        Assert.NotNull(deleted2);
        Assert.True(deleted2.IsDeleted);

        var totalActive = await db.ChatSession.CountAsync(s => s.AspNetUserId == userId && !s.IsDeleted, ct);
        Assert.Equal(3, totalActive);
    }

    [Fact]
    public async Task TogglePinSession_EnforcesMaxPinnedLimit()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateInMemoryDbContext();
        var config = CreateTestConfiguration(); // MaxPinnedSessionsPerUser = 2
        var service = new ChatRetentionService(db, config, NullLogger<ChatRetentionService>.Instance);
        var userId = "user1";

        db.ChatSession.Add(new ChatSession { Id = 1, AspNetUserId = userId, Title = "Chat 1", IsPinned = true });
        db.ChatSession.Add(new ChatSession { Id = 2, AspNetUserId = userId, Title = "Chat 2", IsPinned = true });
        db.ChatSession.Add(new ChatSession { Id = 3, AspNetUserId = userId, Title = "Chat 3", IsPinned = false });
        await db.SaveChangesAsync(ct);

        // Attempting to pin a 3rd session should throw InvalidOperationException
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.TogglePinSessionAsync(3, userId, ct));

        // Unpinning an existing session should succeed
        var unpinned = await service.TogglePinSessionAsync(1, userId, ct);
        Assert.False(unpinned);

        // Now pinning chat 3 should succeed
        var pinnedNow = await service.TogglePinSessionAsync(3, userId, ct);
        Assert.True(pinnedNow);
    }

    [Fact]
    public async Task SoftDeleteAndRestore_Session_WorksCorrectly()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateInMemoryDbContext();
        var config = CreateTestConfiguration();
        var service = new ChatRetentionService(db, config, NullLogger<ChatRetentionService>.Instance);
        var userId = "user1";

        db.ChatSession.Add(new ChatSession { Id = 1, AspNetUserId = userId, Title = "Chat 1", IsDeleted = false });
        await db.SaveChangesAsync(ct);

        // Soft delete
        var deleted = await service.SoftDeleteSessionAsync(1, userId, "User", ct);
        Assert.True(deleted);

        var session = await db.ChatSession.FindAsync(new object[] { (long)1 }, ct);
        Assert.NotNull(session);
        Assert.True(session.IsDeleted);
        Assert.NotNull(session.DeletedUtc);
        Assert.Equal("User", session.DeletionReason);

        // Restore
        var restored = await service.RestoreSessionAsync(1, userId, ct);
        Assert.True(restored);

        session = await db.ChatSession.FindAsync(new object[] { (long)1 }, ct);
        Assert.NotNull(session);
        Assert.False(session.IsDeleted);
        Assert.Null(session.DeletedUtc);
        Assert.Null(session.DeletionReason);
    }

    [Fact]
    public async Task EnforceUserSessionQuota_ReturnsZero_WhenWithinQuota()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateInMemoryDbContext();
        var config = CreateTestConfiguration(); // MaxActiveSessionsPerUser = 3
        var service = new ChatRetentionService(db, config, NullLogger<ChatRetentionService>.Instance);
        var userId = "user1";

        db.ChatSession.Add(new ChatSession { Id = 1, AspNetUserId = userId, Title = "Chat 1", IsDeleted = false });
        db.ChatSession.Add(new ChatSession { Id = 2, AspNetUserId = userId, Title = "Chat 2", IsDeleted = false });
        await db.SaveChangesAsync(ct);

        int softDeleted = await service.EnforceUserSessionQuotaAsync(userId, ct);
        Assert.Equal(0, softDeleted);
    }

    [Fact]
    public async Task RestoreSession_WhenAtQuota_ThrowsInvalidOperationException()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateInMemoryDbContext();
        var config = CreateTestConfiguration(); // MaxActiveSessionsPerUser = 3
        var service = new ChatRetentionService(db, config, NullLogger<ChatRetentionService>.Instance);
        var userId = "user1";

        // 3 active unpinned sessions (at quota limit of 3)
        db.ChatSession.Add(new ChatSession { Id = 1, AspNetUserId = userId, Title = "Oldest Active", LastMessageUtc = DateTime.UtcNow.AddDays(-10), IsDeleted = false });
        db.ChatSession.Add(new ChatSession { Id = 2, AspNetUserId = userId, Title = "Active 2", LastMessageUtc = DateTime.UtcNow.AddDays(-5), IsDeleted = false });
        db.ChatSession.Add(new ChatSession { Id = 3, AspNetUserId = userId, Title = "Active 3", LastMessageUtc = DateTime.UtcNow, IsDeleted = false });
        // 1 soft-deleted session in trash
        db.ChatSession.Add(new ChatSession { Id = 4, AspNetUserId = userId, Title = "Trash Chat", LastMessageUtc = DateTime.UtcNow.AddDays(-2), IsDeleted = true });
        await db.SaveChangesAsync(ct);

        // Restoring Chat 4 when already at max active quota (3) should throw InvalidOperationException
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RestoreSessionAsync(4, userId, ct));
        Assert.Contains("active chat quota", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Chat 4 should still be soft-deleted
        var chat4 = await db.ChatSession.FindAsync(new object[] { (long)4 }, ct);
        Assert.NotNull(chat4);
        Assert.True(chat4.IsDeleted);

        // Chat 1 should still be active (not auto-deleted)
        var chat1 = await db.ChatSession.FindAsync(new object[] { (long)1 }, ct);
        Assert.NotNull(chat1);
        Assert.False(chat1.IsDeleted);

        var activeCount = await db.ChatSession.CountAsync(s => s.AspNetUserId == userId && !s.IsDeleted, ct);
        Assert.Equal(3, activeCount);
    }

    [Fact]
    public async Task TogglePinSession_WhenUnpinned_DoesNotModifyActiveQuota()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateInMemoryDbContext();
        var config = CreateTestConfiguration(); // MaxActiveSessionsPerUser = 3, MaxPinned = 2
        var service = new ChatRetentionService(db, config, NullLogger<ChatRetentionService>.Instance);
        var userId = "user1";

        // 2 unpinned + 1 pinned (total 3 active, at limit 3)
        db.ChatSession.Add(new ChatSession { Id = 1, AspNetUserId = userId, Title = "Unpinned 1", LastMessageUtc = DateTime.UtcNow.AddDays(-10), IsPinned = false, IsDeleted = false });
        db.ChatSession.Add(new ChatSession { Id = 2, AspNetUserId = userId, Title = "Unpinned 2", LastMessageUtc = DateTime.UtcNow.AddDays(-5), IsPinned = false, IsDeleted = false });
        db.ChatSession.Add(new ChatSession { Id = 3, AspNetUserId = userId, Title = "Pinned Chat", LastMessageUtc = DateTime.UtcNow.AddDays(-1), IsPinned = true, IsDeleted = false });
        await db.SaveChangesAsync(ct);

        // Unpinning Chat 3 makes it unpinned. Total active count remains 3 and zero chats are deleted.
        var isPinned = await service.TogglePinSessionAsync(3, userId, ct);
        Assert.False(isPinned);

        var chat1 = await db.ChatSession.FindAsync(new object[] { (long)1 }, ct);
        Assert.NotNull(chat1);
        Assert.False(chat1.IsDeleted);

        var activeCount = await db.ChatSession.CountAsync(s => s.AspNetUserId == userId && !s.IsDeleted, ct);
        Assert.Equal(3, activeCount);
    }

    [Fact]
    public async Task BulkSoftDeleteSessions_ExcludesPinned_WhenIncludePinnedIsFalse()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateInMemoryDbContext();
        var config = CreateTestConfiguration();
        var service = new ChatRetentionService(db, config, NullLogger<ChatRetentionService>.Instance);
        var userId = "user1";

        db.ChatSession.Add(new ChatSession { Id = 1, AspNetUserId = userId, Title = "Unpinned 1", IsPinned = false, IsDeleted = false });
        db.ChatSession.Add(new ChatSession { Id = 2, AspNetUserId = userId, Title = "Unpinned 2", IsPinned = false, IsDeleted = false });
        db.ChatSession.Add(new ChatSession { Id = 3, AspNetUserId = userId, Title = "Pinned 1", IsPinned = true, IsDeleted = false });
        db.ChatSession.Add(new ChatSession { Id = 4, AspNetUserId = "otherUser", Title = "Other User Chat", IsPinned = false, IsDeleted = false });
        await db.SaveChangesAsync(ct);

        int count = await service.BulkSoftDeleteSessionsAsync(userId, includePinned: false, "User", ct);
        Assert.Equal(2, count);

        var unpinned1 = await db.ChatSession.FindAsync(new object[] { (long)1 }, ct);
        var unpinned2 = await db.ChatSession.FindAsync(new object[] { (long)2 }, ct);
        var pinned1 = await db.ChatSession.FindAsync(new object[] { (long)3 }, ct);
        var other = await db.ChatSession.FindAsync(new object[] { (long)4 }, ct);

        Assert.True(unpinned1!.IsDeleted);
        Assert.Equal("User", unpinned1.DeletionReason);
        Assert.NotNull(unpinned1.DeletedUtc);

        Assert.True(unpinned2!.IsDeleted);
        Assert.False(pinned1!.IsDeleted);
        Assert.False(other!.IsDeleted);
    }

    [Fact]
    public async Task BulkSoftDeleteSessions_IncludesPinned_WhenIncludePinnedIsTrue()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateInMemoryDbContext();
        var config = CreateTestConfiguration();
        var service = new ChatRetentionService(db, config, NullLogger<ChatRetentionService>.Instance);
        var userId = "user1";

        db.ChatSession.Add(new ChatSession { Id = 1, AspNetUserId = userId, Title = "Unpinned 1", IsPinned = false, IsDeleted = false });
        db.ChatSession.Add(new ChatSession { Id = 2, AspNetUserId = userId, Title = "Pinned 1", IsPinned = true, IsDeleted = false });
        await db.SaveChangesAsync(ct);

        int count = await service.BulkSoftDeleteSessionsAsync(userId, includePinned: true, "User", ct);
        Assert.Equal(2, count);

        var s1 = await db.ChatSession.FindAsync(new object[] { (long)1 }, ct);
        var s2 = await db.ChatSession.FindAsync(new object[] { (long)2 }, ct);

        Assert.True(s1!.IsDeleted);
        Assert.True(s2!.IsDeleted);
    }

    [Fact]
    public async Task UnpinAllSessions_ClearsPinnedFlagOnAllActiveSessions()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateInMemoryDbContext();
        var config = CreateTestConfiguration();
        var service = new ChatRetentionService(db, config, NullLogger<ChatRetentionService>.Instance);
        var userId = "user1";

        db.ChatSession.Add(new ChatSession { Id = 1, AspNetUserId = userId, Title = "Pinned 1", IsPinned = true, IsDeleted = false });
        db.ChatSession.Add(new ChatSession { Id = 2, AspNetUserId = userId, Title = "Pinned 2", IsPinned = true, IsDeleted = false });
        db.ChatSession.Add(new ChatSession { Id = 3, AspNetUserId = userId, Title = "Unpinned", IsPinned = false, IsDeleted = false });
        db.ChatSession.Add(new ChatSession { Id = 4, AspNetUserId = "otherUser", Title = "Other Pinned", IsPinned = true, IsDeleted = false });
        await db.SaveChangesAsync(ct);

        int count = await service.UnpinAllSessionsAsync(userId, ct);
        Assert.Equal(2, count);

        var s1 = await db.ChatSession.FindAsync(new object[] { (long)1 }, ct);
        var s2 = await db.ChatSession.FindAsync(new object[] { (long)2 }, ct);
        var s3 = await db.ChatSession.FindAsync(new object[] { (long)3 }, ct);
        var s4 = await db.ChatSession.FindAsync(new object[] { (long)4 }, ct);

        Assert.False(s1!.IsPinned);
        Assert.False(s2!.IsPinned);
        Assert.False(s3!.IsPinned);
        Assert.True(s4!.IsPinned);
    }
}
