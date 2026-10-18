using Microsoft.Extensions.Caching.Memory;

namespace Overseer.Services.Legal;

/// <summary>
/// Holds a GnollHack handoff's greeting while its user has not accepted the Terms of Service, so the
/// greeting can start once they accept. Keyed by session and user, so one user cannot start another's.
/// Lost on restart.
/// </summary>
public sealed class PendingGreetingStore
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private readonly IMemoryCache _cache;

    public PendingGreetingStore(IMemoryCache cache)
    {
        _cache = cache;
    }

    private static string Key(long sessionId, string userId) => $"pending-greeting:{sessionId}:{userId}";

    public void Hold(long sessionId, string userId, string prompt)
        => _cache.Set(Key(sessionId, userId), prompt, new MemoryCacheEntryOptions
        {
            Size = 1,
            AbsoluteExpirationRelativeToNow = Lifetime
        });

    /// <summary>The held greeting, given back once; null when there is none for this session and user.</summary>
    public string? TryTake(long sessionId, string userId)
    {
        string key = Key(sessionId, userId);
        if (_cache.TryGetValue(key, out string? prompt))
        {
            _cache.Remove(key);
            return prompt;
        }

        return null;
    }
}
