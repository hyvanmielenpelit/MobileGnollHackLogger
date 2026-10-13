namespace Overseer.Services.Telemetry;

using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;

/// <summary>What one event adds to a day's activity row.</summary>
public readonly record struct ActivityDelta(
    int Chats = 0, int ConfidentialChats = 0, int GnollHackChats = 0,
    int UserMessages = 0, int AssistantMessages = 0,
    int IncognitoChats = 0, int IncognitoUserMessages = 0);

/// <summary>Counts chats and messages per user per UTC day, and incognito ones on an anonymous daily row.</summary>
public interface IActivityCounter
{
    /// <summary>Adds <paramref name="delta"/> to the UTC day's row of <paramref name="userId"/> (null: the anonymous row). Never throws.</summary>
    Task TryAddAsync(string? userId, ActivityDelta delta, DateTime utcNow);
}

/// <summary>
/// Adds to <see cref="OverseerDailyActivity"/> rows in a scope of its own with one atomic UPDATE, so concurrent turns
/// never lose an increment; inserts the day's row when the update found none, and repeats the update once when two
/// first events of a day race on the unique index. A failure is logged and never reaches a chat.
/// </summary>
public sealed class ActivityCounter : IActivityCounter
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ActivityCounter> _logger;

    public ActivityCounter(IServiceScopeFactory scopeFactory, ILogger<ActivityCounter> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task TryAddAsync(string? userId, ActivityDelta delta, DateTime utcNow)
    {
        if (delta == default) return;

        try
        {
            var day = DateOnly.FromDateTime(utcNow);
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            if (await UpdateAsync(db, day, userId, delta) > 0) return;

            db.OverseerDailyActivities.Add(new OverseerDailyActivity
            {
                DateUtc = day,
                AspNetUserId = userId,
                ChatsCreated = delta.Chats,
                ConfidentialChatsCreated = delta.ConfidentialChats,
                GnollHackChatsCreated = delta.GnollHackChats,
                UserMessages = delta.UserMessages,
                AssistantMessages = delta.AssistantMessages,
                IncognitoChatsCreated = delta.IncognitoChats,
                IncognitoUserMessages = delta.IncognitoUserMessages
            });

            try
            {
                await db.SaveChangesAsync(CancellationToken.None);
            }
            catch (DbUpdateException)
            {
                // Another first event of the day inserted the row; add to it instead.
                db.ChangeTracker.Clear();
                await UpdateAsync(db, day, userId, delta);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not count Overseer activity.");
        }
    }

    private static Task<int> UpdateAsync(ApplicationDbContext db, DateOnly day, string? userId, ActivityDelta delta)
    {
        var query = userId == null
            ? db.OverseerDailyActivities.Where(a => a.DateUtc == day && a.AspNetUserId == null)
            : db.OverseerDailyActivities.Where(a => a.DateUtc == day && a.AspNetUserId == userId);

        return query.ExecuteUpdateAsync(s => s
            .SetProperty(a => a.ChatsCreated, a => a.ChatsCreated + delta.Chats)
            .SetProperty(a => a.ConfidentialChatsCreated, a => a.ConfidentialChatsCreated + delta.ConfidentialChats)
            .SetProperty(a => a.GnollHackChatsCreated, a => a.GnollHackChatsCreated + delta.GnollHackChats)
            .SetProperty(a => a.UserMessages, a => a.UserMessages + delta.UserMessages)
            .SetProperty(a => a.AssistantMessages, a => a.AssistantMessages + delta.AssistantMessages)
            .SetProperty(a => a.IncognitoChatsCreated, a => a.IncognitoChatsCreated + delta.IncognitoChats)
            .SetProperty(a => a.IncognitoUserMessages, a => a.IncognitoUserMessages + delta.IncognitoUserMessages),
            CancellationToken.None);
    }
}
