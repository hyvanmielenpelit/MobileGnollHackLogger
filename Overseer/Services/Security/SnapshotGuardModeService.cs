namespace Overseer.Services.Security;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MobileGnollHackLogger.Data;
using Overseer.Models;

/// <summary>The snapshot guard's mode as the Security tab shows it.</summary>
/// <param name="SetAtUtc">When an administrator last set it; null while <paramref name="Seeded"/>.</param>
/// <param name="SetByUserId">Who last set it; null while <paramref name="Seeded"/>.</param>
/// <param name="Seeded">True while no row exists and <c>SnapshotGuard:Mode</c> decides.</param>
public sealed record SnapshotGuardModeState(SnapshotGuardMode Mode, DateTime? SetAtUtc, string? SetByUserId, bool Seeded);

/// <summary>
/// The snapshot guard's runtime mode, over the append-only <see cref="SnapshotGuardSetting"/> rows. Read on every
/// scan, so every entry point follows an administrator's change.
/// </summary>
/// <remarks>
/// The mode is cached for <see cref="CacheDuration"/> and written through on <see cref="SetAsync"/>, so a change is
/// immediate in this process; the expiry picks up a change made outside it. A stored value that is not a mode
/// reads as <see cref="SnapshotGuardMode.Enforce"/>.
/// </remarks>
public sealed class SnapshotGuardModeService
{
    internal static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    private const string CacheKey = "Overseer.SnapshotGuard.Mode";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _time;

    public SnapshotGuardModeService(IServiceScopeFactory scopeFactory, IMemoryCache cache, IConfiguration configuration)
        : this(scopeFactory, cache, configuration, TimeProvider.System)
    {
    }

    internal SnapshotGuardModeService(IServiceScopeFactory scopeFactory, IMemoryCache cache, IConfiguration configuration, TimeProvider time)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _configuration = configuration;
        _time = time;
    }

    /// <summary>
    /// The cached mode. On a miss it reads the newest row, or the seed when there is none.
    /// <see cref="SnapshotGuardMode.Enforce"/>, and not cached, when the state cannot be read.
    /// </summary>
    public SnapshotGuardMode Mode()
    {
        if (_cache.TryGetValue(CacheKey, out CachedMode? cached) && cached != null && _time.GetUtcNow() < cached.ExpiresAt)
        {
            return cached.Mode;
        }

        SnapshotGuardModeState state;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = db.SnapshotGuardSettings.AsNoTracking().OrderByDescending(s => s.Id).FirstOrDefault();
            state = ToState(row);
        }
        catch (Exception)
        {
            return SnapshotGuardMode.Enforce;
        }

        Store(state.Mode);
        return state.Mode;
    }

    /// <summary>The current state, read from the database, for the Security tab.</summary>
    public async Task<SnapshotGuardModeState> GetStateAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.SnapshotGuardSettings.AsNoTracking().OrderByDescending(s => s.Id).FirstOrDefaultAsync(ct);
        var state = ToState(row);
        Store(state.Mode);
        return state;
    }

    /// <summary>
    /// Appends a row and writes the cache through. Setting the mode it already has still appends a row, so the
    /// history shows the click.
    /// </summary>
    public async Task<SnapshotGuardModeState> SetAsync(SnapshotGuardMode mode, string? userId, CancellationToken ct = default)
    {
        var row = new SnapshotGuardSetting
        {
            Mode = mode.ToString(),
            SetAtUtc = _time.GetUtcNow().UtcDateTime,
            SetByUserId = string.IsNullOrEmpty(userId) ? null : userId
        };

        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.SnapshotGuardSettings.Add(row);
            await db.SaveChangesAsync(ct);
        }

        Store(mode);
        return ToState(row);
    }

    private SnapshotGuardModeState ToState(SnapshotGuardSetting? row)
        => row == null
            ? new SnapshotGuardModeState(SnapshotGuardOptions.Read(_configuration).Mode, null, null, Seeded: true)
            : new SnapshotGuardModeState(SnapshotGuardOptions.ParseMode(row.Mode) ?? SnapshotGuardMode.Enforce,
                DateTime.SpecifyKind(row.SetAtUtc, DateTimeKind.Utc), row.SetByUserId, Seeded: false);

    private void Store(SnapshotGuardMode mode)
    {
        _cache.Set(CacheKey, new CachedMode(mode, _time.GetUtcNow() + CacheDuration), new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheDuration,
            Size = 1
        });
    }

    // The expiry is checked against the injected clock; the cache's own expiry only evicts the entry.
    private sealed record CachedMode(SnapshotGuardMode Mode, DateTimeOffset ExpiresAt);
}
