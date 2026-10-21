namespace Overseer.Services.Feedback;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MobileGnollHackLogger.Data;

/// <summary>The feedback switch as the admin card shows it.</summary>
/// <param name="Enabled">Whether the production chat is offered <c>report_feedback</c>.</param>
/// <param name="SetAtUtc">When an administrator last set it; null while <paramref name="Seeded"/>.</param>
/// <param name="SetByUserId">Who last set it; null while <paramref name="Seeded"/>.</param>
/// <param name="Seeded">True while no setting row exists and <c>Feedback:Enabled</c> decides.</param>
public sealed record FeedbackSwitchState(bool Enabled, DateTime? SetAtUtc, string? SetByUserId, bool Seeded);

/// <summary>
/// The runtime switch that offers or withholds <c>report_feedback</c> in the production chat, over the
/// append-only <see cref="OverseerFeedbackSetting"/> rows. GnollBench candidates are not governed by it.
/// </summary>
/// <remarks>
/// The state is cached for <see cref="CacheDuration"/> and written through on <see cref="SetAsync"/>,
/// so a change is immediate in this process; the expiry picks up a change made outside it.
/// </remarks>
public sealed class FeedbackSwitchService
{
    internal static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    private const string CacheKey = "Overseer.FeedbackSwitch";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _time;

    public FeedbackSwitchService(IServiceScopeFactory scopeFactory, IMemoryCache cache, IConfiguration configuration)
        : this(scopeFactory, cache, configuration, TimeProvider.System)
    {
    }

    internal FeedbackSwitchService(IServiceScopeFactory scopeFactory, IMemoryCache cache, IConfiguration configuration, TimeProvider time)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _configuration = configuration;
        _time = time;
    }

    /// <summary>
    /// The cached state. On a miss it reads the newest setting row, or <c>Feedback:Enabled</c> when there
    /// is none. False, and not cached, when the state cannot be read.
    /// </summary>
    public bool IsEnabled
    {
        get
        {
            if (_cache.TryGetValue(CacheKey, out CachedSwitch? cached) && cached != null && _time.GetUtcNow() < cached.ExpiresAt)
            {
                return cached.Enabled;
            }

            FeedbackSwitchState state;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var row = db.OverseerFeedbackSettings.AsNoTracking().OrderByDescending(s => s.Id).FirstOrDefault();
                state = ToState(row);
            }
            catch (Exception)
            {
                return false;
            }

            Store(state.Enabled);
            return state.Enabled;
        }
    }

    /// <summary>The current state, read from the database, for the admin card.</summary>
    public async Task<FeedbackSwitchState> GetStateAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.OverseerFeedbackSettings.AsNoTracking().OrderByDescending(s => s.Id).FirstOrDefaultAsync(ct);
        var state = ToState(row);
        Store(state.Enabled);
        return state;
    }

    /// <summary>
    /// Appends a setting row and writes the cache through. Setting the value it already has still
    /// appends a row, so the history shows the click.
    /// </summary>
    public async Task<FeedbackSwitchState> SetAsync(bool enabled, string? userId, CancellationToken ct = default)
    {
        var row = new OverseerFeedbackSetting
        {
            Enabled = enabled,
            SetAtUtc = _time.GetUtcNow().UtcDateTime,
            SetByUserId = string.IsNullOrEmpty(userId) ? null : userId
        };

        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.OverseerFeedbackSettings.Add(row);
            await db.SaveChangesAsync(ct);
        }

        Store(enabled);
        return ToState(row);
    }

    private FeedbackSwitchState ToState(OverseerFeedbackSetting? row)
        => row == null
            ? new FeedbackSwitchState(FeedbackSettings.Read(_configuration).Enabled, null, null, Seeded: true)
            : new FeedbackSwitchState(row.Enabled, DateTime.SpecifyKind(row.SetAtUtc, DateTimeKind.Utc), row.SetByUserId, Seeded: false);

    private void Store(bool enabled)
    {
        _cache.Set(CacheKey, new CachedSwitch(enabled, _time.GetUtcNow() + CacheDuration), new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheDuration,
            Size = 1
        });
    }

    // The expiry is checked against the injected clock; the cache's own expiry only evicts the entry.
    private sealed record CachedSwitch(bool Enabled, DateTimeOffset ExpiresAt);
}
