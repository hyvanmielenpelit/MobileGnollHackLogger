using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Services;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// Exercises <see cref="DatabaseStorageMetricsService"/> against a non-relational in-memory
/// provider. Every SQL Server specific query fails there, which is precisely the point: it
/// drives the service down its detection-failure and caching paths without needing a live
/// database.
/// </summary>
public class DatabaseStorageMetricsServiceTests
{
    private static ApplicationDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    /// <summary>
    /// Mirrors the production registration in Program.cs, which sets a global SizeLimit.
    /// A size-limited MemoryCache rejects any entry that does not declare its own Size.
    /// </summary>
    private static IMemoryCache CreateSizeLimitedCache()
        => new MemoryCache(new MemoryCacheOptions { SizeLimit = 10000 });

    private static IConfiguration CreateConfiguration(Dictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>();
        if (overrides != null)
        {
            foreach (var kv in overrides)
            {
                settings[kv.Key] = kv.Value;
            }
        }

        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    private static DatabaseStorageMetricsService CreateService(
        ApplicationDbContext db,
        IMemoryCache cache,
        IConfiguration? configuration = null)
    {
        return new DatabaseStorageMetricsService(
            db,
            configuration ?? CreateConfiguration(),
            cache,
            NullLogger<DatabaseStorageMetricsService>.Instance,
            new ServiceCollection().BuildServiceProvider());
    }

    /// <summary>
    /// Regression test for the crash where edition detection cached its result with the
    /// convenience Set(key, value, TimeSpan) overload, which does not set an entry Size.
    /// Against the production cache configuration that throws
    /// "Cache entry must specify a value for Size when SizeLimit is set", taking down the
    /// whole Database tab.
    /// </summary>
    [Fact]
    public async Task GetStorageMetricsAsync_WithSizeLimitedCache_DoesNotThrow()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateInMemoryDbContext();
        using var cache = (MemoryCache)CreateSizeLimitedCache();
        var service = CreateService(db, cache);

        var metrics = await service.GetStorageMetricsAsync(ct);

        Assert.NotNull(metrics);
    }

    [Fact]
    public async Task GetStorageMetricsAsync_WhenDetectionFails_ReportsConservativeFallback()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateInMemoryDbContext();
        using var cache = (MemoryCache)CreateSizeLimitedCache();
        var service = CreateService(db, cache);

        var metrics = await service.GetStorageMetricsAsync(ct);

        // Detection cannot succeed on a non-relational provider, so the panel must fall back
        // to the conservative Express figure and say so rather than silently guessing.
        Assert.Equal("Fallback", metrics.LimitSource);
        Assert.Equal(10240, metrics.MaxLimitMb);
        Assert.True(metrics.HasEngineSizeLimit);
        Assert.Equal("SQL Server (edition undetected)", metrics.ServerProductLabel);
        Assert.Equal("Normal", metrics.StatusLevel);
    }

    [Fact]
    public async Task GetStorageMetricsAsync_CachesDetectionResult()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateInMemoryDbContext();
        using var cache = (MemoryCache)CreateSizeLimitedCache();
        var service = CreateService(db, cache);

        await service.GetStorageMetricsAsync(ct);
        var countAfterFirst = cache.Count;
        await service.GetStorageMetricsAsync(ct);

        // The second call must reuse the cached entry rather than adding another one,
        // and must still not throw against the size-limited cache.
        Assert.Equal(1, countAfterFirst);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public async Task GetStorageMetricsAsync_HonoursConfiguredOverride()
    {
        var ct = TestContext.Current.CancellationToken;
        using var db = CreateInMemoryDbContext();
        using var cache = (MemoryCache)CreateSizeLimitedCache();
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            { "ChatRetentionSettings:DatabaseMaxSizeMbOverride", "51200" }
        });
        var service = CreateService(db, cache, configuration);

        var metrics = await service.GetStorageMetricsAsync(ct);

        // This is the rehearsal path for the SQL Server 2025 Express upgrade.
        Assert.Equal(51200, metrics.MaxLimitMb);
        Assert.Equal("Configured", metrics.LimitSource);
    }
}
