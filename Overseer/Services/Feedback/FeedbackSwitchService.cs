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

/// <summary>One feedback switch as the admin card shows it.</summary>
/// <param name="Enabled">Whether the switch's surface is offered <c>report_feedback</c>.</param>
/// <param name="SetAtUtc">When an administrator last set it; null while <paramref name="Seeded"/>.</param>
/// <param name="SetByUserId">Who last set it; null while <paramref name="Seeded"/>.</param>
/// <param name="Seeded">True while no row of this target exists and the seed decides.</param>
public sealed record FeedbackSwitchState(bool Enabled, DateTime? SetAtUtc, string? SetByUserId, bool Seeded);

/// <summary>
/// The two runtime switches that offer or withhold <c>report_feedback</c>, one per
/// <see cref="OverseerFeedbackSwitchTarget"/>, over the append-only <see cref="OverseerFeedbackSetting"/>
/// rows. The chat switch is read on every turn; the GnollBench switch once per run launch, after which
/// the run's recorded prompt options govern it.
/// </summary>
/// <remarks>
/// Each state is cached for <see cref="CacheDuration"/> and written through on <see cref="SetAsync"/>,
/// so a change is immediate in this process; the expiry picks up a change made outside it.
/// </remarks>
public sealed class FeedbackSwitchService
{
    internal static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    private const string CacheKeyPrefix = "Overseer.FeedbackSwitch.";

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
    /// The cached state of <paramref name="target"/>. On a miss it reads the newest row of that target,
    /// or the seed when there is none. False, and not cached, when the state cannot be read.
    /// </summary>
    public bool IsEnabled(OverseerFeedbackSwitchTarget target)
    {
        ThrowIfUnknown(target);
        if (_cache.TryGetValue(CacheKey(target), out CachedSwitch? cached) && cached != null && _time.GetUtcNow() < cached.ExpiresAt)
        {
            return cached.Enabled;
        }

        FeedbackSwitchState state;
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = db.OverseerFeedbackSettings.AsNoTracking().Where(s => s.Target == target).OrderByDescending(s => s.Id).FirstOrDefault();
            state = ToState(target, row);
        }
        catch (Exception)
        {
            return false;
        }

        Store(target, state.Enabled);
        return state.Enabled;
    }

    /// <summary>The current state of <paramref name="target"/>, read from the database, for the admin card.</summary>
    public async Task<FeedbackSwitchState> GetStateAsync(OverseerFeedbackSwitchTarget target, CancellationToken ct = default)
    {
        ThrowIfUnknown(target);
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.OverseerFeedbackSettings.AsNoTracking().Where(s => s.Target == target).OrderByDescending(s => s.Id).FirstOrDefaultAsync(ct);
        var state = ToState(target, row);
        Store(target, state.Enabled);
        return state;
    }

    /// <summary>
    /// Appends a setting row for <paramref name="target"/> and writes its cache through. Setting the
    /// value it already has still appends a row, so the history shows the click.
    /// </summary>
    public async Task<FeedbackSwitchState> SetAsync(OverseerFeedbackSwitchTarget target, bool enabled, string? userId, CancellationToken ct = default)
    {
        ThrowIfUnknown(target);
        var row = new OverseerFeedbackSetting
        {
            Target = target,
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

        Store(target, enabled);
        return ToState(target, row);
    }

    private static string CacheKey(OverseerFeedbackSwitchTarget target) => CacheKeyPrefix + target;

    private static void ThrowIfUnknown(OverseerFeedbackSwitchTarget target)
    {
        if (target is not (OverseerFeedbackSwitchTarget.Chat or OverseerFeedbackSwitchTarget.GnollBench))
        {
            throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown feedback switch.");
        }
    }

    // With no row the chat switch follows Feedback:Enabled and the GnollBench switch is on.
    private FeedbackSwitchState ToState(OverseerFeedbackSwitchTarget target, OverseerFeedbackSetting? row)
        => row == null
            ? new FeedbackSwitchState(target == OverseerFeedbackSwitchTarget.Chat ? FeedbackSettings.Read(_configuration).Enabled : true, null, null, Seeded: true)
            : new FeedbackSwitchState(row.Enabled, DateTime.SpecifyKind(row.SetAtUtc, DateTimeKind.Utc), row.SetByUserId, Seeded: false);

    private void Store(OverseerFeedbackSwitchTarget target, bool enabled)
    {
        _cache.Set(CacheKey(target), new CachedSwitch(enabled, _time.GetUtcNow() + CacheDuration), new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheDuration,
            Size = 1
        });
    }

    // The expiry is checked against the injected clock; the cache's own expiry only evicts the entry.
    private sealed record CachedSwitch(bool Enabled, DateTimeOffset ExpiresAt);
}
