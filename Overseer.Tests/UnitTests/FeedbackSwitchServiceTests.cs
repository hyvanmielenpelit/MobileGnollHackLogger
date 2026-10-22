namespace Overseer.Tests.UnitTests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MobileGnollHackLogger.Data;
using Overseer.Services.Feedback;
using Xunit;

/// <summary>
/// The runtime chat feedback switch: seeded from <c>Feedback:Enabled</c> until an administrator sets it,
/// append-only with the newest chat row winning, written through to its cache entry, and re-read once
/// that entry expires. Rows of the retired GnollBench target are never read.
/// </summary>
public sealed class FeedbackSwitchServiceTests : IDisposable
{
    private const OverseerFeedbackSwitchTarget Chat = OverseerFeedbackSwitchTarget.Chat;
    private const OverseerFeedbackSwitchTarget GnollBench = OverseerFeedbackSwitchTarget.GnollBench;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _databaseName = Guid.NewGuid().ToString();
    private readonly ServiceProvider _provider;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 10000 });
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero));

    public FeedbackSwitchServiceTests()
    {
        string databaseName = _databaseName;
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(databaseName));
        _provider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _cache.Dispose();
        _provider.Dispose();
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now;

        public ManualClock(DateTimeOffset now) => _now = now;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private static IConfiguration Configuration(bool enabled)
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Feedback:Enabled"] = enabled ? "true" : "false"
        }).Build();

    private FeedbackSwitchService CreateService(bool configuredEnabled = true)
        => new(_provider.GetRequiredService<IServiceScopeFactory>(), _cache, Configuration(configuredEnabled), _clock);

    private async Task AddRowDirectlyAsync(bool enabled, OverseerFeedbackSwitchTarget? target = Chat)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = new OverseerFeedbackSetting { Enabled = enabled, SetAtUtc = _clock.GetUtcNow().UtcDateTime, SetByUserId = "elsewhere" };
        if (target.HasValue) row.Target = target.Value;
        db.OverseerFeedbackSettings.Add(row);
        await db.SaveChangesAsync(Ct);
    }

    private async Task<List<OverseerFeedbackSetting>> RowsAsync()
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.OverseerFeedbackSettings.AsNoTracking().OrderBy(s => s.Id).ToListAsync(Ct);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WithNoRow_IsEnabledFollowsTheConfiguration_AndTheStateIsSeeded(bool configured)
    {
        var service = CreateService(configured);

        Assert.Equal(configured, service.IsEnabled());

        var state = await service.GetStateAsync(Ct);
        Assert.Equal(new FeedbackSwitchState(configured, null, null, Seeded: true), state);
        Assert.Empty(await RowsAsync());
    }

    [Fact]
    public void ThePublicConstructor_TakesTheThreeServices()
    {
        var service = new FeedbackSwitchService(_provider.GetRequiredService<IServiceScopeFactory>(), _cache, Configuration(true));

        Assert.True(service.IsEnabled());
    }

    [Fact]
    public async Task SetAsyncFalse_AppendsARow_AndIsEnabledIsFalseAtOnce()
    {
        var service = CreateService(configuredEnabled: true);
        Assert.True(service.IsEnabled());

        var state = await service.SetAsync(false, "admin-1", Ct);

        Assert.False(service.IsEnabled());
        Assert.False(state.Enabled);
        Assert.False(state.Seeded);
        Assert.Equal("admin-1", state.SetByUserId);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, state.SetAtUtc);

        var row = Assert.Single(await RowsAsync());
        Assert.False(row.Enabled);
        Assert.Equal(Chat, row.Target);
        Assert.Equal("admin-1", row.SetByUserId);
    }

    [Fact]
    public async Task ASecondSetAsync_AppendsAnotherRow_AndTheNewestWins()
    {
        var service = CreateService();

        await service.SetAsync(false, "admin-1", Ct);
        _clock.Advance(TimeSpan.FromMinutes(1));
        await service.SetAsync(true, "admin-2", Ct);

        Assert.True(service.IsEnabled());
        var rows = await RowsAsync();
        Assert.Equal(new[] { false, true }, rows.Select(r => r.Enabled));

        // A fresh instance reads the same newest row.
        var state = await CreateService(configuredEnabled: false).GetStateAsync(Ct);
        Assert.True(state.Enabled);
        Assert.Equal("admin-2", state.SetByUserId);
        Assert.False(state.Seeded);
    }

    [Fact]
    public async Task SettingTheSameValueAgain_StillAppendsARow()
    {
        var service = CreateService();

        await service.SetAsync(true, "admin-1", Ct);
        await service.SetAsync(true, "admin-1", Ct);

        Assert.Equal(2, (await RowsAsync()).Count);
    }

    [Fact]
    public async Task TheCachedValue_IsServedWithinTheExpiry_AndReReadAfterIt()
    {
        var service = CreateService(configuredEnabled: true);
        Assert.True(service.IsEnabled());

        // A change made outside this service is not seen while the entry is fresh: no query ran.
        await AddRowDirectlyAsync(enabled: false);
        _clock.Advance(FeedbackSwitchService.CacheDuration - TimeSpan.FromSeconds(1));
        Assert.True(service.IsEnabled());

        _clock.Advance(TimeSpan.FromSeconds(2));
        Assert.False(service.IsEnabled());
    }

    [Fact]
    public async Task ARetiredGnollBenchRow_DoesNotAffectTheChatSwitch()
    {
        await AddRowDirectlyAsync(enabled: true);
        _clock.Advance(TimeSpan.FromMinutes(1));
        await AddRowDirectlyAsync(enabled: false, target: GnollBench);
        var service = CreateService(configuredEnabled: false);

        Assert.True(service.IsEnabled());
        var state = await service.GetStateAsync(Ct);
        var chatRow = (await RowsAsync()).First(r => r.Target == Chat);
        Assert.Equal(new FeedbackSwitchState(true, chatRow.SetAtUtc, "elsewhere", Seeded: false), state);
    }

    [Fact]
    public async Task ARowWithoutATarget_ReadsAsTheChatSwitch()
    {
        await AddRowDirectlyAsync(enabled: false, target: null);
        var service = CreateService(configuredEnabled: true);

        Assert.False(service.IsEnabled());
        var state = await service.GetStateAsync(Ct);
        Assert.False(state.Seeded);
        Assert.Equal("elsewhere", state.SetByUserId);
    }
}
