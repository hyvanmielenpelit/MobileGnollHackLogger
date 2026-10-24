using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Services.Privacy;
using Overseer.Services.Security;
using Overseer.Services.Tools;

namespace Overseer.Tests.Helpers;

/// <summary>A snapshot guard over its own in-memory database, for the tests of the endpoints and tools that call it.</summary>
internal sealed class SnapshotGuardTestServices : IDisposable
{
    public sealed class RecordingEvents : ISecurityEventRecorder
    {
        public List<SecurityEventEntry> Entries { get; } = new();
        public void Record(SecurityEventEntry entry) { lock (Entries) Entries.Add(entry); }
    }

    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 1000 });

    /// <param name="mode">The seeded mode: Off, Observe or Enforce.</param>
    /// <param name="configure">More services, such as the ephemeral session store.</param>
    public SnapshotGuardTestServices(string mode, Action<IServiceCollection>? configure = null, string? databaseName = null)
    {
        Configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SnapshotGuard:Mode"] = mode,
            ["SnapshotGuard:AutoBlockEnabled"] = "false",
            ["SnapshotGuard:NotificationEmail"] = ""
        }).Build();

        string name = databaseName ?? "snapshot-guard-" + Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(name));
        services.AddSingleton(Configuration);
        services.AddSingleton<IToolHandler>(new FakeChangeToolHandler("report_feedback"));
        services.AddSingleton<IToolHandler>(new FakeChangeToolHandler("wiki_search"));
        configure?.Invoke(services);
        Provider = services.BuildServiceProvider();

        var scopes = Provider.GetRequiredService<IServiceScopeFactory>();
        Guard = new SnapshotGuardService(scopes, new SnapshotGuardModeService(scopes, _cache, Configuration),
            new OverseerAccessService(scopes, _cache, Events), Configuration, NullLogger<SnapshotGuardService>.Instance, Provider, Events);
    }

    public IConfiguration Configuration { get; }
    public ServiceProvider Provider { get; }
    public RecordingEvents Events { get; } = new();
    public SnapshotGuardService Guard { get; }

    public void Dispose()
    {
        Provider.Dispose();
        _cache.Dispose();
    }
}
