namespace GnollHackServer.Data.Security;

/// <summary>Limits of the game API sign-in throttle. Windows and blocks default to 15 minutes.</summary>
public sealed record GameApiSignInThrottleOptions(
    int AccountFailureLimit = 10, TimeSpan? AccountWindow = null, TimeSpan? AccountBlock = null,
    int IpFailureLimit = 100, TimeSpan? IpWindow = null, TimeSpan? IpBlock = null,
    bool PerIpLimitsEnabled = true, int MaxTrackedKeys = 100_000);

/// <summary>Whether a sign-in may be checked now; when not, how long until it may and why ("account" or "ip").</summary>
public readonly record struct ThrottleDecision(bool Allowed, TimeSpan RetryAfter, string? Reason);

/// <summary>
/// Failed-password throttle for the GnollHack game APIs, kept in memory and separate from Identity lockout so a
/// guessing attack cannot send a player's game client into a lockout retry loop. Sliding failure windows per account
/// (upper-cased user name, whether or not the account exists, so the throttle reveals nothing) and per IP address;
/// reaching a limit opens a block of fixed length. A refused request is not counted and does not extend anything.
/// Thread-safe; at most <see cref="GameApiSignInThrottleOptions.MaxTrackedKeys"/> keys, oldest dropped.
/// </summary>
public sealed class GameApiSignInThrottle
{
    private sealed class Window
    {
        public readonly Queue<DateTime> Failures = new();
        public DateTime? BlockedUntil;
        public DateTime LastTouched;
    }

    private readonly GameApiSignInThrottleOptions _options;
    private readonly TimeProvider _time;
    private readonly Dictionary<string, Window> _accounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Window> _ips = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    private TimeSpan AccountWindow => _options.AccountWindow ?? TimeSpan.FromMinutes(15);
    private TimeSpan AccountBlock => _options.AccountBlock ?? TimeSpan.FromMinutes(15);
    private TimeSpan IpWindow => _options.IpWindow ?? TimeSpan.FromMinutes(15);
    private TimeSpan IpBlock => _options.IpBlock ?? TimeSpan.FromMinutes(15);

    public GameApiSignInThrottle(GameApiSignInThrottleOptions? options = null, TimeProvider? time = null)
    {
        _options = options ?? new GameApiSignInThrottleOptions();
        _time = time ?? TimeProvider.System;
    }

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    private static string AccountKey(string userName) => (userName ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>Called before the password is checked; a refused request is not counted and does not extend any window.</summary>
    public ThrottleDecision Check(string normalizedUserName, string? ipAddress)
    {
        DateTime now = Now;
        lock (_gate)
        {
            if (_accounts.TryGetValue(AccountKey(normalizedUserName), out var account)
                && account.BlockedUntil is DateTime accountUntil && accountUntil > now)
            {
                return new ThrottleDecision(false, accountUntil - now, "account");
            }

            if (_options.PerIpLimitsEnabled && !string.IsNullOrEmpty(ipAddress)
                && _ips.TryGetValue(ipAddress, out var ip)
                && ip.BlockedUntil is DateTime ipUntil && ipUntil > now)
            {
                return new ThrottleDecision(false, ipUntil - now, "ip");
            }

            return new ThrottleDecision(true, TimeSpan.Zero, null);
        }
    }

    public void RecordFailure(string normalizedUserName, string? ipAddress)
    {
        DateTime now = Now;
        lock (_gate)
        {
            Count(_accounts, AccountKey(normalizedUserName), now, AccountWindow, _options.AccountFailureLimit, AccountBlock);
            if (_options.PerIpLimitsEnabled && !string.IsNullOrEmpty(ipAddress))
            {
                Count(_ips, ipAddress, now, IpWindow, _options.IpFailureLimit, IpBlock);
            }
        }
    }

    /// <summary>A correct password clears the account's failures; the IP's are kept.</summary>
    public void RecordSuccess(string normalizedUserName)
    {
        lock (_gate)
        {
            _accounts.Remove(AccountKey(normalizedUserName));
        }
    }

    private void Count(Dictionary<string, Window> map, string key, DateTime now, TimeSpan window, int limit, TimeSpan block)
    {
        if (!map.TryGetValue(key, out var w))
        {
            Sweep(map, now, window);
            w = new Window();
            map[key] = w;
        }
        w.LastTouched = now;

        if (w.BlockedUntil is DateTime until && until > now)
        {
            return;
        }
        if (w.BlockedUntil != null)
        {
            w.BlockedUntil = null;
            w.Failures.Clear();
        }

        while (w.Failures.Count > 0 && now - w.Failures.Peek() >= window)
        {
            w.Failures.Dequeue();
        }
        w.Failures.Enqueue(now);

        if (w.Failures.Count >= limit)
        {
            w.BlockedUntil = now + block;
            w.Failures.Clear();
        }
    }

    /// <summary>
    /// Before a new key is added to a full map: drops keys whose window and block have passed, then the least
    /// recently touched keys until there is room.
    /// </summary>
    private void Sweep(Dictionary<string, Window> map, DateTime now, TimeSpan window)
    {
        int cap = Math.Max(1, _options.MaxTrackedKeys);
        if (map.Count < cap) return;

        foreach (var stale in map.Where(p => (p.Value.BlockedUntil == null || p.Value.BlockedUntil <= now)
                && now - p.Value.LastTouched >= window).Select(p => p.Key).ToList())
        {
            map.Remove(stale);
        }

        int excess = map.Count - (cap - 1);
        if (excess > 0)
        {
            foreach (var oldest in map.OrderBy(p => p.Value.LastTouched).Take(excess).Select(p => p.Key).ToList())
            {
                map.Remove(oldest);
            }
        }
    }

    /// <summary>Account keys tracked now; at most <see cref="GameApiSignInThrottleOptions.MaxTrackedKeys"/>.</summary>
    public int TrackedAccountCount
    {
        get { lock (_gate) return _accounts.Count; }
    }
}
