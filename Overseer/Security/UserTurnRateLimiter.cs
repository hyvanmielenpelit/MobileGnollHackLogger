using System.Threading.RateLimiting;

namespace Overseer.Security;

/// <summary>
/// The <see cref="RateLimitPolicies.Chat"/> budget for chat turns started outside that policy's
/// endpoints: a fixed window of <c>PrivacySettings:RateLimits:ChatPermitsPerMinute</c> turns per
/// minute, partitioned by user name.
/// </summary>
public sealed class UserTurnRateLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter;

    public UserTurnRateLimiter(IConfiguration configuration)
    {
        int permitsPerMinute = configuration.GetValue("PrivacySettings:RateLimits:ChatPermitsPerMinute", 30);

        _limiter = PartitionedRateLimiter.Create<string, string>(userName =>
            RateLimitPartition.GetFixedWindowLimiter(userName, _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = permitsPerMinute,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1)
            }), StringComparer.Ordinal);
    }

    /// <summary>Takes one permit for <paramref name="userName"/>; false when the window's permits are spent.</summary>
    public bool TryAcquire(string userName)
    {
        using RateLimitLease lease = _limiter.AttemptAcquire(userName);
        return lease.IsAcquired;
    }

    public void Dispose() => _limiter.Dispose();
}
