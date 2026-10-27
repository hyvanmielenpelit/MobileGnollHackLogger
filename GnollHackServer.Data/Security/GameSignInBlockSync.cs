using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MobileGnollHackLogger.Data;

namespace GnollHackServer.Data.Security;

/// <summary>
/// The outcome of a game sign-in check: allowed, or refused with the retry time, the reason ("pair", "account" or
/// "ip") and, when the database knows the block, its account.
/// </summary>
public readonly record struct GameSignInCheck(bool Allowed, TimeSpan RetryAfter, string? Reason, string? UserId);

/// <summary>
/// Mirrors the pair and account blocks of existing accounts from one process's <see cref="GameApiSignInThrottle"/> to
/// the <c>GameSignInBlocks</c> table, and lets an administrator's clear there release the block in memory. The
/// throttle stays authoritative for counting; the table only carries blocks out and clears back in. A refused
/// request reads the block's row at most once per <see cref="LookupInterval"/>; a cleared row releases the block and
/// the request goes on to the password check. Address blocks and names with no account are never written. One
/// instance per process (singleton). Checking and recording never throw: on a database error the throttle's
/// in-memory answer stands and the error is logged.
/// </summary>
public sealed class GameSignInBlockSync
{
    /// <summary>How long a refused block key goes without another database read.</summary>
    public static readonly TimeSpan LookupInterval = TimeSpan.FromSeconds(15);

    /// <summary>How long a row is kept after its block ended.</summary>
    public static readonly TimeSpan RowRetention = TimeSpan.FromDays(1);

    private const int MaxCachedLookups = 10_000;

    /// <summary>Blocks one check may release: a pair and an account block cleared together need two.</summary>
    private const int MaxReleasesPerCheck = 3;

    private readonly GameApiSignInThrottle _throttle;
    private readonly GameSignInBlockSource _source;
    private readonly TimeProvider _time;
    private readonly Dictionary<string, (DateTime CheckedAt, string? UserId)> _lookups = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public GameSignInBlockSync(GameApiSignInThrottle throttle, GameSignInBlockSource source, TimeProvider? time = null)
    {
        _throttle = throttle;
        _source = source;
        _time = time ?? TimeProvider.System;
    }

    public GameApiSignInThrottle Throttle => _throttle;

    public GameSignInBlockSource Source => _source;

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    /// <summary>
    /// Called before the password is checked. <paramref name="normalizedUserName"/> is the caller's
    /// <c>UserManager.NormalizeName</c> of <paramref name="userName"/>, which finds the account's rows.
    /// </summary>
    public async Task<GameSignInCheck> CheckAsync(ApplicationDbContext db, string userName, string normalizedUserName, string? ip,
        ILogger? logger = null, CancellationToken ct = default)
    {
        for (int released = 0; ; released++)
        {
            var decision = _throttle.Check(userName, ip);
            if (decision.Allowed)
            {
                return new GameSignInCheck(true, TimeSpan.Zero, null, null);
            }
            if (KindOf(decision.Reason) is not GameSignInBlockKind kind || released == MaxReleasesPerCheck)
            {
                return new GameSignInCheck(false, decision.RetryAfter, decision.Reason, null);
            }

            var (cleared, userId) = await LookupAsync(db, kind, userName, normalizedUserName, ip, logger, ct);
            if (!cleared)
            {
                return new GameSignInCheck(false, decision.RetryAfter, decision.Reason, userId);
            }
            _throttle.Release(decision.Reason!, userName, ip);
            Forget(kind, userName, ip);
        }
    }

    /// <summary>
    /// Counts a wrong password. A pair or account block it opens for an existing account (<paramref name="userId"/>
    /// not null) is written as an active row, and the account's rows of this process that ended more than
    /// <see cref="RowRetention"/> ago are removed.
    /// </summary>
    public async Task RecordFailureAsync(ApplicationDbContext db, string userName, string? ip, string? userId,
        ILogger? logger = null, CancellationToken ct = default)
    {
        var opened = _throttle.RecordFailure(userName, ip);
        if (userId == null || !opened.Any(o => KindOf(o.Kind) != null))
        {
            return;
        }

        var touched = new List<GameSignInBlock>();
        try
        {
            foreach (var block in opened)
            {
                if (KindOf(block.Kind) is not GameSignInBlockKind kind) continue;

                string? rowIp = kind == GameSignInBlockKind.Pair ? Cut(ip) : null;
                var rows = await db.GameSignInBlocks
                    .Where(b => b.Source == _source && b.Kind == kind && b.AspNetUserId == userId && b.IpAddress == rowIp)
                    .ToListAsync(ct);
                if (rows.Count == 0)
                {
                    var row = new GameSignInBlock { Source = _source, Kind = kind, AspNetUserId = userId, IpAddress = rowIp };
                    db.GameSignInBlocks.Add(row);
                    rows.Add(row);
                }
                foreach (var row in rows)
                {
                    row.BlockedUtc = block.BlockedUtc;
                    row.BlockedUntilUtc = block.BlockedUntilUtc;
                    row.ClearedUtc = null;
                    row.ClearedByUserId = null;
                }
                touched.AddRange(rows);
                Forget(kind, userName, ip);
            }

            DateTime cutoff = Now - RowRetention;
            var ended = await db.GameSignInBlocks
                .Where(b => b.AspNetUserId == userId && b.Source == _source && b.BlockedUntilUtc < cutoff)
                .ToListAsync(ct);
            db.GameSignInBlocks.RemoveRange(ended);
            touched.AddRange(ended);

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Leave the caller's context usable: a rejected row would fail every later save.
            foreach (var row in touched)
            {
                try { db.Entry(row).State = EntityState.Detached; } catch { }
            }
            logger?.LogWarning(ex, "Could not record the game sign-in block of user {UserId}.", userId);
        }
    }

    /// <summary>A correct password clears the pair's and the account's failures in the throttle.</summary>
    public void RecordSuccess(string userName, string? ip) => _throttle.RecordSuccess(userName, ip);

    /// <summary>
    /// Releases one block of this process at once, for an administrator's clear made in this same process, and
    /// forgets its lookup so the next check does not wait for <see cref="LookupInterval"/>.
    /// </summary>
    public void ReleaseLocal(GameSignInBlockKind kind, string userName, string? ip)
    {
        _throttle.Release(kind == GameSignInBlockKind.Pair ? "pair" : "account", userName, ip);
        Forget(kind, userName, ip);
    }

    /// <summary>
    /// At start-up: a restart has cleared every in-memory block, so this process's active rows are ended now, and
    /// its rows that ended more than <see cref="RowRetention"/> ago are removed. Tracked updates, so the InMemory
    /// provider runs it too. Throws on a database error.
    /// </summary>
    public async Task EndAllOwnAsync(ApplicationDbContext db, CancellationToken ct = default)
    {
        DateTime now = Now;
        DateTime cutoff = now - RowRetention;
        var rows = await db.GameSignInBlocks
            .Where(b => b.Source == _source
                && ((b.ClearedUtc == null && b.BlockedUntilUtc > now) || b.BlockedUntilUtc < cutoff))
            .ToListAsync(ct);
        foreach (var row in rows)
        {
            if (row.BlockedUntilUtc < cutoff)
            {
                db.GameSignInBlocks.Remove(row);
            }
            else
            {
                row.BlockedUntilUtc = now;
            }
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Whether the row of the throttle's current block is cleared, and its account. A row whose end differs from the
    /// block's in memory is an earlier block's, and never releases this one.
    /// </summary>
    private async Task<(bool Cleared, string? UserId)> LookupAsync(ApplicationDbContext db, GameSignInBlockKind kind,
        string userName, string normalizedUserName, string? ip, ILogger? logger, CancellationToken ct)
    {
        DateTime now = Now;
        string key = LookupKey(kind, userName, ip);
        lock (_gate)
        {
            if (_lookups.TryGetValue(key, out var cached) && now - cached.CheckedAt < LookupInterval)
            {
                return (false, cached.UserId);
            }
        }

        string? userId = null;
        try
        {
            string? rowIp = Cut(ip);
            var query = db.GameSignInBlocks.Where(b => b.Source == _source && b.Kind == kind
                && b.AspNetUser!.NormalizedUserName == normalizedUserName);
            if (kind == GameSignInBlockKind.Pair)
            {
                query = query.Where(b => b.IpAddress == rowIp);
            }
            var row = await query.OrderByDescending(b => b.BlockedUtc)
                .Select(b => new { b.AspNetUserId, b.BlockedUntilUtc, b.ClearedUtc })
                .FirstOrDefaultAsync(ct);

            if (row != null)
            {
                userId = row.AspNetUserId;
                DateTime? blockEnd = _throttle.BlockEnd(kind == GameSignInBlockKind.Pair ? "pair" : "account", userName, ip);
                if (row.ClearedUtc != null && blockEnd == row.BlockedUntilUtc)
                {
                    return (true, userId);
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Could not read the game sign-in block of a refused request.");
        }

        lock (_gate)
        {
            if (_lookups.Count >= MaxCachedLookups)
            {
                foreach (var stale in _lookups.Where(p => now - p.Value.CheckedAt >= LookupInterval).Select(p => p.Key).ToList())
                {
                    _lookups.Remove(stale);
                }
                if (_lookups.Count >= MaxCachedLookups)
                {
                    foreach (var oldest in _lookups.OrderBy(p => p.Value.CheckedAt).Take(_lookups.Count - MaxCachedLookups + 1)
                        .Select(p => p.Key).ToList())
                    {
                        _lookups.Remove(oldest);
                    }
                }
            }
            _lookups[key] = (now, userId);
        }
        return (false, userId);
    }

    private void Forget(GameSignInBlockKind kind, string userName, string? ip)
    {
        lock (_gate)
        {
            _lookups.Remove(LookupKey(kind, userName, ip));
        }
    }

    /// <summary>Keyed as the throttle keys its windows: the upper-cased name, and the address for a pair.</summary>
    private static string LookupKey(GameSignInBlockKind kind, string userName, string? ip) =>
        (kind == GameSignInBlockKind.Pair ? "pair\n" : "account\n") + (userName ?? string.Empty).Trim().ToUpperInvariant()
            + (kind == GameSignInBlockKind.Pair ? "\n" + (ip ?? string.Empty) : string.Empty);

    private static GameSignInBlockKind? KindOf(string? reason) => reason switch
    {
        "pair" => GameSignInBlockKind.Pair,
        "account" => GameSignInBlockKind.Account,
        _ => null,
    };

    private static string? Cut(string? value) => value == null ? null : value.Length <= 45 ? value : value[..45];
}
