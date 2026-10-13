using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace GnollHackServer.Data.Security;

/// <summary>
/// Limits of the game API sign-in throttle. Pair (account and address): 10 failures in 15 minutes block for 15 minutes.
/// Account, across addresses: 50 in 60 minutes block for 30. Address, across accounts: 100 in 15 minutes block for 15.
/// </summary>
public sealed record GameApiSignInThrottleOptions(
    int PairFailureLimit = 10, TimeSpan? PairWindow = null, TimeSpan? PairBlock = null,
    int AccountFailureLimit = 50, TimeSpan? AccountWindow = null, TimeSpan? AccountBlock = null,
    int IpFailureLimit = 100, TimeSpan? IpWindow = null, TimeSpan? IpBlock = null,
    bool PerIpLimitsEnabled = true, int MaxTrackedKeys = 100_000)
{
    public const string SectionName = "GameApiSecurity";

    /// <summary>
    /// Reads the <c>GameApiSecurity</c> section: <c>PairFailureLimit</c>, <c>PairWindowMinutes</c>,
    /// <c>PairBlockMinutes</c>, <c>AccountFailureLimit</c>, <c>AccountWindowMinutes</c>, <c>AccountBlockMinutes</c>,
    /// <c>IpFailureLimit</c>, <c>IpWindowMinutes</c>, <c>IpBlockMinutes</c> and <c>PerIpLimitsEnabled</c>. A missing
    /// or unreadable value takes the default.
    /// </summary>
    public static GameApiSignInThrottleOptions FromConfiguration(IConfiguration configuration)
    {
        int Int(string key, int fallback) =>
            int.TryParse(configuration[SectionName + ":" + key], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value : fallback;
        TimeSpan Minutes(string key, int fallback) => TimeSpan.FromMinutes(Int(key, fallback));
        bool Bool(string key, bool fallback) =>
            bool.TryParse(configuration[SectionName + ":" + key], out bool value) ? value : fallback;

        return new GameApiSignInThrottleOptions(
            PairFailureLimit: Int("PairFailureLimit", 10),
            PairWindow: Minutes("PairWindowMinutes", 15),
            PairBlock: Minutes("PairBlockMinutes", 15),
            AccountFailureLimit: Int("AccountFailureLimit", 50),
            AccountWindow: Minutes("AccountWindowMinutes", 60),
            AccountBlock: Minutes("AccountBlockMinutes", 30),
            IpFailureLimit: Int("IpFailureLimit", 100),
            IpWindow: Minutes("IpWindowMinutes", 15),
            IpBlock: Minutes("IpBlockMinutes", 15),
            PerIpLimitsEnabled: Bool("PerIpLimitsEnabled", true));
    }
}

/// <summary>Whether a sign-in may be checked now; when not, how long until it may and why ("pair", "account" or "ip").</summary>
public readonly record struct ThrottleDecision(bool Allowed, TimeSpan RetryAfter, string? Reason);

/// <summary>
/// Failed-password throttle for the GnollHack game APIs, kept in memory and separate from Identity lockout so a
/// guessing attack cannot send a player's game client into a lockout retry loop. Sliding failure windows per pair of
/// account and address (the tight limit a retrying client meets), per account across addresses (loose enough that
/// one other address cannot reach it), and per IP address across accounts. The account is the upper-cased user name,
/// whether or not the account exists, so the throttle reveals nothing. Reaching a limit opens a block of fixed
/// length. A refused request is not counted and does not extend anything. Thread-safe; at most
/// <see cref="GameApiSignInThrottleOptions.MaxTrackedKeys"/> keys per limit, oldest dropped.
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
    private readonly Dictionary<string, Window> _pairs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Window> _accounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Window> _ips = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    private TimeSpan PairWindow => _options.PairWindow ?? TimeSpan.FromMinutes(15);
    private TimeSpan PairBlock => _options.PairBlock ?? TimeSpan.FromMinutes(15);
    private TimeSpan AccountWindow => _options.AccountWindow ?? TimeSpan.FromMinutes(60);
    private TimeSpan AccountBlock => _options.AccountBlock ?? TimeSpan.FromMinutes(30);
    private TimeSpan IpWindow => _options.IpWindow ?? TimeSpan.FromMinutes(15);
    private TimeSpan IpBlock => _options.IpBlock ?? TimeSpan.FromMinutes(15);

    public GameApiSignInThrottle(GameApiSignInThrottleOptions? options = null, TimeProvider? time = null)
    {
        _options = options ?? new GameApiSignInThrottleOptions();
        _time = time ?? TimeProvider.System;
    }

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    private static string AccountKey(string userName) => (userName ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>The account key and the address; a missing address is a key of its own.</summary>
    private static string PairKey(string userName, string? ipAddress) => AccountKey(userName) + "\n" + (ipAddress ?? string.Empty);

    private bool TracksIp(string? ipAddress) => _options.PerIpLimitsEnabled && !string.IsNullOrEmpty(ipAddress);

    /// <summary>Called before the password is checked; a refused request is not counted and does not extend any window.</summary>
    public ThrottleDecision Check(string normalizedUserName, string? ipAddress)
    {
        DateTime now = Now;
        lock (_gate)
        {
            if (BlockedUntil(_pairs, PairKey(normalizedUserName, ipAddress), now) is DateTime pairUntil)
            {
                return new ThrottleDecision(false, pairUntil - now, "pair");
            }

            if (BlockedUntil(_accounts, AccountKey(normalizedUserName), now) is DateTime accountUntil)
            {
                return new ThrottleDecision(false, accountUntil - now, "account");
            }

            if (TracksIp(ipAddress) && BlockedUntil(_ips, ipAddress!, now) is DateTime ipUntil)
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
            Count(_pairs, PairKey(normalizedUserName, ipAddress), now, PairWindow, _options.PairFailureLimit, PairBlock);
            Count(_accounts, AccountKey(normalizedUserName), now, AccountWindow, _options.AccountFailureLimit, AccountBlock);
            if (TracksIp(ipAddress))
            {
                Count(_ips, ipAddress!, now, IpWindow, _options.IpFailureLimit, IpBlock);
            }
        }
    }

    /// <summary>A correct password clears the pair's and the account's failures; the IP's are kept.</summary>
    public void RecordSuccess(string normalizedUserName, string? ipAddress)
    {
        lock (_gate)
        {
            _pairs.Remove(PairKey(normalizedUserName, ipAddress));
            _accounts.Remove(AccountKey(normalizedUserName));
        }
    }

    private static DateTime? BlockedUntil(Dictionary<string, Window> map, string key, DateTime now) =>
        map.TryGetValue(key, out var w) && w.BlockedUntil is DateTime until && until > now ? until : null;

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

    /// <summary>Account-and-address keys tracked now; at most <see cref="GameApiSignInThrottleOptions.MaxTrackedKeys"/>.</summary>
    public int TrackedPairCount
    {
        get { lock (_gate) return _pairs.Count; }
    }
}
