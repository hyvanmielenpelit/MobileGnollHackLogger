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
        var config = CreateTestConfiguration();
        var service = new ChatRetentionService(db, config, NullLogger<ChatRetentionService>.Instance);
        var userId = "user1";

        // Create 4 sessions, oldest is pinned
        db.ChatSession.Add(new ChatSession { Id = 1, AspNetUserId = userId, Title = "Pinned Old Chat", LastMessageUtc = DateTime.UtcNow.AddDays(-20), IsPinned = true });
        db.ChatSession.Add(new ChatSession { Id = 2, AspNetUserId = userId, Title = "Unpinned 1", LastMessageUtc = DateTime.UtcNow.AddDays(-10), IsPinned = false });
        db.ChatSession.Add(new ChatSession { Id = 3, AspNetUserId = userId, Title = "Unpinned 2", LastMessageUtc = DateTime.UtcNow.AddDays(-5), IsPinned = false });
        db.ChatSession.Add(new ChatSession { Id = 4, AspNetUserId = userId, Title = "Unpinned 3", LastMessageUtc = DateTime.UtcNow.AddDays(-2), IsPinned = false });
        db.ChatSession.Add(new ChatSession { Id = 5, AspNetUserId = userId, Title = "Unpinned 4", LastMessageUtc = DateTime.UtcNow, IsPinned = false });
        await db.SaveChangesAsync(ct);

        // 4 unpinned sessions (quota is 3), oldest unpinned is Chat 2
        int softDeleted = await service.EnforceUserSessionQuotaAsync(userId, ct);

        Assert.Equal(1, softDeleted);

        var pinned = await db.ChatSession.FindAsync(new object[] { (long)1 }, ct);
        Assert.NotNull(pinned);
        Assert.False(pinned.IsDeleted, "Pinned session must NOT be soft-deleted by quota enforcement.");

        var deleted = await db.ChatSession.FindAsync(new object[] { (long)2 }, ct);
        Assert.NotNull(deleted);
        Assert.True(deleted.IsDeleted);
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
}
