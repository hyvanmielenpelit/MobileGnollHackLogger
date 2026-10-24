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
using Overseer.Models;
using Overseer.Services.Security;
using Xunit;

/// <summary>
/// The snapshot guard's runtime mode: seeded from <c>SnapshotGuard:Mode</c> until an administrator sets it,
/// append-only with the newest row winning, written through to its cache entry, re-read once that entry expires,
/// and closed (Enforce) for a value it cannot read.
/// </summary>
public sealed class SnapshotGuardModeServiceTests : IDisposable
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _databaseName = Guid.NewGuid().ToString();
    private readonly ServiceProvider _provider;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 10000 });
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero));

    public SnapshotGuardModeServiceTests()
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

    private static IConfiguration Configuration(string? mode)
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SnapshotGuard:Mode"] = mode
        }).Build();

    private SnapshotGuardModeService CreateService(string? seed = "Observe")
        => new(_provider.GetRequiredService<IServiceScopeFactory>(), _cache, Configuration(seed), _clock);

    private async Task AddRowDirectlyAsync(string mode)
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.SnapshotGuardSettings.Add(new SnapshotGuardSetting { Mode = mode, SetAtUtc = _clock.GetUtcNow().UtcDateTime, SetByUserId = "elsewhere" });
        await db.SaveChangesAsync(Ct);
    }

    private async Task<List<SnapshotGuardSetting>> RowsAsync()
    {
        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.SnapshotGuardSettings.AsNoTracking().OrderBy(s => s.Id).ToListAsync(Ct);
    }

    [Theory]
    [InlineData("Off", SnapshotGuardMode.Off)]
    [InlineData("observe", SnapshotGuardMode.Observe)]
    [InlineData(" Enforce ", SnapshotGuardMode.Enforce)]
    [InlineData("Loud", SnapshotGuardMode.Enforce)]
    [InlineData(null, SnapshotGuardMode.Enforce)]
    public async Task WithNoRow_TheSeedDecides_AndAnUnreadableSeedIsEnforce(string? seed, SnapshotGuardMode expected)
    {
        var service = CreateService(seed);

        Assert.Equal(expected, service.Mode());
        Assert.Equal(new SnapshotGuardModeState(expected, null, null, Seeded: true), await service.GetStateAsync(Ct));
        Assert.Empty(await RowsAsync());
    }

    [Fact]
    public async Task SetAsync_AppendsARow_AndModeSeesItAtOnce()
    {
        var service = CreateService("Observe");
        Assert.Equal(SnapshotGuardMode.Observe, service.Mode());

        var state = await service.SetAsync(SnapshotGuardMode.Enforce, "admin-1", Ct);

        Assert.Equal(SnapshotGuardMode.Enforce, service.Mode());
        Assert.Equal(new SnapshotGuardModeState(SnapshotGuardMode.Enforce, _clock.GetUtcNow().UtcDateTime, "admin-1", Seeded: false), state);
        var row = Assert.Single(await RowsAsync());
        Assert.Equal("Enforce", row.Mode);
    }

    [Fact]
    public async Task SettingTheSameModeTwice_AppendsTwoRows_AndTheNewestWins()
    {
        var service = CreateService();

        await service.SetAsync(SnapshotGuardMode.Off, "admin-1", Ct);
        await service.SetAsync(SnapshotGuardMode.Off, "admin-1", Ct);
        _clock.Advance(TimeSpan.FromMinutes(1));
        await service.SetAsync(SnapshotGuardMode.Enforce, "admin-2", Ct);

        Assert.Equal(new[] { "Off", "Off", "Enforce" }, (await RowsAsync()).Select(r => r.Mode));
        var state = await CreateService("Off").GetStateAsync(Ct);
        Assert.Equal(SnapshotGuardMode.Enforce, state.Mode);
        Assert.Equal("admin-2", state.SetByUserId);
    }

    [Fact]
    public async Task TheCachedMode_IsServedWithinThirtySeconds_AndReReadAfter()
    {
        var service = CreateService("Observe");
        Assert.Equal(SnapshotGuardMode.Observe, service.Mode());

        await AddRowDirectlyAsync("Enforce");
        _clock.Advance(TimeSpan.FromSeconds(29));
        Assert.Equal(SnapshotGuardMode.Observe, service.Mode());

        _clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(SnapshotGuardMode.Enforce, service.Mode());
        Assert.Equal(TimeSpan.FromSeconds(30), SnapshotGuardModeService.CacheDuration);
    }

    [Fact]
    public async Task AStoredValueThatIsNoMode_ReadsAsEnforce()
    {
        await AddRowDirectlyAsync("Loud");
        var service = CreateService("Off");

        Assert.Equal(SnapshotGuardMode.Enforce, service.Mode());
        Assert.False((await service.GetStateAsync(Ct)).Seeded);
    }

    [Fact]
    public void TheOptions_ReadTheirDefaults_AndClampTheDays()
    {
        var options = SnapshotGuardOptions.Read(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SnapshotGuard:AutoBlockEnabled"] = "maybe",
            ["SnapshotGuard:AutoBlockDistinctDays"] = "99",
            ["SnapshotGuard:AutoBlockWindowDays"] = "x",
            ["SnapshotGuard:NotificationEmail"] = "  "
        }).Build());

        Assert.Equal(new SnapshotGuardOptions(SnapshotGuardMode.Enforce, true, 3, 30, null), options);
        Assert.Null(SnapshotGuardOptions.ParseMode("1"));
    }
}
