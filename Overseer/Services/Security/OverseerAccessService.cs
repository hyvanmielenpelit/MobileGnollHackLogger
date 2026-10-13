using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MobileGnollHackLogger.Data;

namespace Overseer.Services.Security;

/// <summary>An administrator's block on a user's Overseer AI access.</summary>
public sealed record OverseerBlock(DateTime BlockedUtc, string? Reason);

/// <summary>
/// Reads and changes whether a user may start AI work in Overseer. A blocked user keeps sign-in, reading,
/// export, deletion and settings. The block is cached for <see cref="CacheDuration"/> and evicted on change.
/// </summary>
public class OverseerAccessService
{
    public const string BlockedCode = "overseer_blocked";
    public const string BlockedMessage = "Your access to the Overseer AI chat has been suspended by an administrator.";
    public const int MaxReasonLength = 256;
    public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMemoryCache _cache;
    private readonly ISecurityEventRecorder? _securityEvents;

    public OverseerAccessService(IServiceScopeFactory scopeFactory, IMemoryCache cache, ISecurityEventRecorder? securityEvents = null)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _securityEvents = securityEvents;
    }

    private static string CacheKey(string userId) => "overseer-block:" + userId;

    /// <summary>The user's block, or null when the user is not blocked (or does not exist).</summary>
    public virtual async Task<OverseerBlock?> GetBlockAsync(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId)) return null;
        if (_cache.TryGetValue(CacheKey(userId), out OverseerBlock? cached))
        {
            return cached;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.Users.OfType<ApplicationUser>()
            .Where(u => u.Id == userId)
            .Select(u => new { u.OverseerBlockedUtc, u.OverseerBlockReason })
            .FirstOrDefaultAsync(ct);

        var block = row?.OverseerBlockedUtc is DateTime at ? new OverseerBlock(at, row.OverseerBlockReason) : null;

        // The cache has a size limit, so every entry declares a size.
        _cache.Set(CacheKey(userId), block, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheDuration,
            Size = 1
        });
        return block;
    }

    /// <summary>Blocks the user. Returns an error message when refused (the admin's own id, an unknown user, a bad reason).</summary>
    public async Task<string?> BlockAsync(string userId, string reason, string adminId)
    {
        reason = (reason ?? string.Empty).Trim();
        if (string.Equals(userId, adminId, StringComparison.Ordinal))
        {
            return "You cannot block yourself.";
        }
        if (reason.Length == 0 || reason.Length > MaxReasonLength)
        {
            return $"A reason of 1 to {MaxReasonLength} characters is required.";
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.OfType<ApplicationUser>().FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return "User not found.";

        user.OverseerBlockedUtc = DateTime.UtcNow;
        user.OverseerBlockReason = reason;
        user.OverseerBlockedByUserId = adminId;
        await db.SaveChangesAsync();

        _cache.Remove(CacheKey(userId));
        _securityEvents?.Record(new SecurityEventEntry(SecurityEventKind.AdminBlocked, UserId: userId, Detail: reason));
        return null;
    }

    /// <summary>Lifts the user's block. Returns an error message when the user does not exist.</summary>
    public async Task<string?> UnblockAsync(string userId, string adminId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.OfType<ApplicationUser>().FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return "User not found.";

        bool wasBlocked = user.OverseerBlockedUtc != null;
        user.OverseerBlockedUtc = null;
        user.OverseerBlockReason = null;
        user.OverseerBlockedByUserId = null;
        await db.SaveChangesAsync();

        _cache.Remove(CacheKey(userId));
        if (wasBlocked)
        {
            _securityEvents?.Record(new SecurityEventEntry(SecurityEventKind.AdminUnblocked, UserId: userId, Detail: "by " + adminId));
        }
        return null;
    }

    /// <summary>The 403 body a blocked request is answered with.</summary>
    public static object BlockedBody(OverseerBlock block) =>
        new { code = BlockedCode, message = BlockedMessage, reason = block.Reason };
}
