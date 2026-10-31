using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services;
using Overseer.Services.ConfigHealth;
using Overseer.Services.SourceIndexing;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The retired-model alert: one per retired catalog entry that enabled system configurations still
/// name, and none for a configuration that is disabled, custom, on a custom endpoint or simply not in
/// the catalog. The AI spend alert: one for the users whose operator-funded spend in the last 24 hours reached
/// the threshold, administrators excluded. The GnollHack source alerts: a missing SourceCodePath, and a failed
/// makedefs that leaves a generated header missing.
/// </summary>
public class ConfigHealthServiceTests
{
    private const string RetiredModelId = "gemini-3.7-flash";

    private readonly DbContextOptions<ApplicationDbContext> _dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options;

    private ConfigHealthService CreateService()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => new ApplicationDbContext(_dbOptions));
        services.AddSingleton<ModelMetadataService>();
        services.AddSingleton<ModelAvailabilityService>();
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new ConfigHealthService(new ConfigurationBuilder().Build(), scopeFactory);
    }

    private void AddConfigs(params SystemAiApiConfiguration[] configs)
    {
        using var db = new ApplicationDbContext(_dbOptions);
        db.SystemAiApiConfigurations.AddRange(configs);
        db.SaveChanges();
    }

    private static SystemAiApiConfiguration Config(
        string displayName, string modelId, bool isEnabled = true, string? catalogMode = ModelCatalogModes.Catalog, string? baseUrl = null) => new()
    {
        DisplayName = displayName,
        Provider = "Google",
        ModelId = modelId,
        IsEnabled = isEnabled,
        ModelCatalogMode = catalogMode,
        BaseUrl = baseUrl,
        ModelRole = 7
    };

    private async Task<List<SystemAlert>> RetiredAlerts() =>
        (await CreateService().GetSystemAlertsAsync(TestContext.Current.CancellationToken)).Where(a => a.Id.StartsWith("retired-model-", StringComparison.Ordinal)).ToList();

    [Fact]
    public async Task TwoEnabledRetiredConfigurations_RaiseOneGroupedAlert_WithALink()
    {
        AddConfigs(
            Config("Flash", RetiredModelId),
            Config("Flash snapshot", RetiredModelId + "-001"),
            Config("Disabled flash", RetiredModelId, isEnabled: false),
            Config("Custom flash", RetiredModelId, catalogMode: ModelCatalogModes.Custom),
            Config("Gateway flash", RetiredModelId, baseUrl: "https://llm.example.com/v1"));

        var alert = Assert.Single(await RetiredAlerts());

        Assert.Equal("retired-model-Google-gemini-3.7-flash", alert.Id);
        Assert.Equal("warning", alert.Type);
        Assert.Equal(
            "2 system configurations use Gemini 3.7 Flash (gemini-3.7-flash), which was removed from the model catalog on 2026-10-10.",
            alert.Message);
        Assert.Equal("/admin/configs", alert.LinkUrl);
        Assert.Equal("Review in System Configs", alert.LinkText);
    }

    [Fact]
    public async Task OneRetiredConfiguration_IsCountedInTheSingular()
    {
        AddConfigs(Config("Flash", RetiredModelId, catalogMode: null));

        var alert = Assert.Single(await RetiredAlerts());

        Assert.StartsWith("1 system configuration uses Gemini 3.7 Flash (gemini-3.7-flash)", alert.Message);
    }

    [Fact]
    public async Task DisabledCustomEndpointAndNotInCatalogConfigurations_RaiseNoAlert()
    {
        AddConfigs(
            Config("Disabled flash", RetiredModelId, isEnabled: false),
            Config("Custom flash", RetiredModelId, catalogMode: ModelCatalogModes.Custom),
            Config("Gateway flash", RetiredModelId, baseUrl: "https://llm.example.com/v1"),
            Config("Tuned flash", "my-tuned-flash"));

        var alerts = (await CreateService().GetSystemAlertsAsync(TestContext.Current.CancellationToken)).ToList();

        Assert.DoesNotContain(alerts, a => a.Id.StartsWith("retired-model-", StringComparison.Ordinal));
        Assert.DoesNotContain(alerts, a => a.Message.Contains("my-tuned-flash", StringComparison.Ordinal));
    }

    // -- AI spend ------------------------------------------------------------------------------------

    private ConfigHealthService CreateSpendService(decimal alertThreshold)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => new ApplicationDbContext(_dbOptions));
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { { "Admins", "spend-boss" } })
            .Build();

        return new ConfigHealthService(configuration, scopeFactory, null,
            Microsoft.Extensions.Options.Options.Create(new AiUsageGuardOptions { AlertUserDailyCostUsd = alertThreshold }));
    }

    private void AddSpend(string userId, string userName, params (int Role, decimal? Cost, double HoursAgo)[] rows)
    {
        using var db = new ApplicationDbContext(_dbOptions);
        if (db.Users.Find(userId) == null)
        {
            db.Users.Add(new ApplicationUser { Id = userId, UserName = userName, Email = userName + "@example.com" });
        }
        foreach (var (role, cost, hoursAgo) in rows)
        {
            db.SystemAiUsageLogs.Add(new SystemAiUsageLog
            {
                SystemAiApiConfigurationId = 1,
                AspNetUserId = userId,
                TimestampUtc = DateTime.UtcNow.AddHours(-hoursAgo),
                Provider = "OpenAI",
                ModelId = "gpt-spend",
                RoleContext = role,
                EstimatedCost = cost
            });
        }
        db.SaveChanges();
    }

    private async Task<List<SystemAlert>> SpendAlerts(decimal alertThreshold) =>
        (await CreateSpendService(alertThreshold).GetSystemAlertsAsync(TestContext.Current.CancellationToken)).Where(a => a.Id.StartsWith("ai-spend:", StringComparison.Ordinal)).ToList();

    [Fact]
    public async Task AiSpend_AtTheThreshold_RaisesOneAlert_WithTheDateAndCountInItsId()
    {
        AddSpend("spender-1", "spender-one", (1, 15m, 1), (3, 5m, 20));
        AddSpend("spender-2", "spender-two", (2, 30m, 2));
        // Below the threshold once GnollBench and older rows are left out.
        AddSpend("spender-3", "spender-three", (1, 10m, 1), (4, 100m, 1), (1, 100m, 25));

        var before = DateTime.UtcNow;
        var alert = Assert.Single(await SpendAlerts(20m));
        var after = DateTime.UtcNow;

        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        Assert.Contains(alert.Id, new[]
        {
            $"ai-spend:{before.ToString("yyyyMMdd", invariant)}:2",
            $"ai-spend:{after.ToString("yyyyMMdd", invariant)}:2"
        });
        Assert.Equal("warning", alert.Type);
        Assert.Equal("2 users spent over $20.00 on AI in the last 24 hours (highest: $30.00).", alert.Message);
        Assert.Equal("/admin/telemetry/users", alert.LinkUrl);
        Assert.Equal("Review", alert.LinkText);
    }

    [Fact]
    public async Task AiSpend_OneUser_IsCountedInTheSingular()
    {
        AddSpend("spender-1", "spender-one", (1, 20m, 1));

        var alert = Assert.Single(await SpendAlerts(20m));

        Assert.EndsWith(":1", alert.Id, StringComparison.Ordinal);
        Assert.StartsWith("1 user spent over $20.00", alert.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AiSpend_BelowTheThreshold_RaisesNoAlert()
    {
        AddSpend("spender-1", "spender-one", (1, 19.99m, 1), (1, null, 1));

        Assert.Empty(await SpendAlerts(20m));
    }

    [Fact]
    public async Task AiSpend_ByAnAdministrator_RaisesNoAlert()
    {
        AddSpend("boss", "spend-boss", (1, 500m, 1));

        Assert.Empty(await SpendAlerts(20m));
    }

    [Fact]
    public async Task AnAbortedRequest_PropagatesTheCancellation_InsteadOfLoggingAFailedCheck()
    {
        AddSpend("spender-1", "spender-one", (1, 500m, 1));
        using var aborted = new CancellationTokenSource();
        aborted.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateSpendService(20m).GetSystemAlertsAsync(aborted.Token));
    }

    [Fact]
    public async Task AiSpend_WithTheAlertTurnedOff_RaisesNoAlert()
    {
        AddSpend("spender-1", "spender-one", (1, 500m, 1));

        Assert.Empty(await SpendAlerts(0m));
    }

    [Fact]
    public async Task AlertsWithoutALink_LeaveTheLinkFieldsNull()
    {
        var alert = Assert.Single(await new ConfigHealthService(new ConfigurationBuilder().Build())
            .GetSystemAlertsAsync(TestContext.Current.CancellationToken), a => a.Id == "sentry-dsn-missing");

        Assert.Null(alert.LinkUrl);
        Assert.Null(alert.LinkText);
    }

    // -- GnollHack source ----------------------------------------------------------------------------

    /// <summary>A ConfigHealthService whose makedefs state is given instead of read from SourceCodeService.</summary>
    private sealed class MakedefsStateHealthService : ConfigHealthService
    {
        private readonly MakedefsHealthState? _state;

        public MakedefsStateHealthService(IConfiguration configuration, MakedefsHealthState? state)
            : base(configuration)
        {
            _state = state;
        }

        protected override MakedefsHealthState? GetMakedefsState() => _state;
    }

    private static IConfiguration ConfigurationWith(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    private static MakedefsHealthState MakedefsState(MakedefsOutcomeKind? kind, params string[] missingHeaders) =>
        new(kind, null, DateTime.UtcNow, missingHeaders);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SourceCodePathMissing_RaisesAWarning(string? path)
    {
        var alert = Assert.Single(await new ConfigHealthService(ConfigurationWith(("SourceCodePath", path))).GetSystemAlertsAsync(TestContext.Current.CancellationToken),
            a => a.Id == "source-code-path-missing");

        Assert.Equal("warning", alert.Type);
        Assert.Equal("GnollHack source code path is not configured. Set SourceCodePath in configuration settings.", alert.Message);
        Assert.Null(alert.LinkUrl);
    }

    [Fact]
    public async Task SourceCodePathConfigured_RaisesNoAlert()
    {
        var alerts = await new ConfigHealthService(ConfigurationWith(("SourceCodePath", @"C:\repos\GnollHack"))).GetSystemAlertsAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain(alerts, a => a.Id == "source-code-path-missing");
    }

    [Theory]
    [InlineData(MakedefsOutcomeKind.BuildFailed)]
    [InlineData(MakedefsOutcomeKind.RunFailed)]
    public async Task MakedefsFailed_WithAMissingHeader_RaisesAnErrorWithALink(MakedefsOutcomeKind kind)
    {
        var service = new MakedefsStateHealthService(ConfigurationWith(), MakedefsState(kind, "pm.h"));

        var alert = Assert.Single(await service.GetSystemAlertsAsync(TestContext.Current.CancellationToken),a => a.Id == "gnollhack-makedefs-failed");

        Assert.Equal("error", alert.Type);
        Assert.Equal(
            "makedefs could not generate the GnollHack headers (pm.h, onames.h), so the chat cannot see monster and object constants.",
            alert.Message);
        Assert.Equal("/admin/services", alert.LinkUrl);
        Assert.Equal("Open GnollHack Source", alert.LinkText);
    }

    [Theory]
    [InlineData(MakedefsOutcomeKind.BuildFailed, false)]
    [InlineData(MakedefsOutcomeKind.RunFailed, false)]
    [InlineData(MakedefsOutcomeKind.ExecutableMissing, true)]
    [InlineData(MakedefsOutcomeKind.SkippedBranch, true)]
    [InlineData(MakedefsOutcomeKind.UpToDate, true)]
    [InlineData(MakedefsOutcomeKind.Generated, true)]
    [InlineData(null, true)]
    public async Task MakedefsAlert_IsAbsent_UnlessAFailureLeavesAHeaderMissing(MakedefsOutcomeKind? kind, bool headerMissing)
    {
        var state = headerMissing ? MakedefsState(kind, "onames.h") : MakedefsState(kind);
        var service = new MakedefsStateHealthService(ConfigurationWith(), state);

        Assert.DoesNotContain(await service.GetSystemAlertsAsync(TestContext.Current.CancellationToken), a => a.Id == "gnollhack-makedefs-failed");
    }

    [Fact]
    public async Task MakedefsAlert_IsAbsent_WhenTheSourceServiceIsUnavailable()
    {
        Assert.DoesNotContain(await new MakedefsStateHealthService(ConfigurationWith(), null).GetSystemAlertsAsync(TestContext.Current.CancellationToken),
            a => a.Id == "gnollhack-makedefs-failed");
        Assert.DoesNotContain(await CreateService().GetSystemAlertsAsync(TestContext.Current.CancellationToken), a => a.Id == "gnollhack-makedefs-failed");
    }

    // -- The snapshot guard ---------------------------------------------------------------------------

    private ConfigHealthService CreateGuardService(string? mode)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { { "SnapshotGuard:Mode", mode } })
            .Build();
        var services = new ServiceCollection();
        services.AddScoped(_ => new ApplicationDbContext(_dbOptions));
        services.AddSingleton<Microsoft.Extensions.Caching.Memory.IMemoryCache>(new Microsoft.Extensions.Caching.Memory.MemoryCache(
            new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions { SizeLimit = 100 }));
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<Overseer.Services.Security.SnapshotGuardModeService>();
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        return new ConfigHealthService(configuration, scopeFactory);
    }

    private long AddSecurityEvent(SecurityEventKind kind, string userId, DateTime at, int count = 1)
    {
        using var db = new ApplicationDbContext(_dbOptions);
        var securityEvent = new SecurityEvent { Kind = kind, AspNetUserId = userId, OccurredUtc = at, LastOccurredUtc = at, Count = count, Code = "injection-own" };
        db.SecurityEvents.Add(securityEvent);
        db.SaveChanges();
        return securityEvent.Id;
    }

    [Theory]
    [InlineData("Observe", "snapshot-guard-observe")]
    [InlineData("Off", "snapshot-guard-off")]
    public async Task TheGuardMode_IsAnAlertWhileItIsNotEnforce(string mode, string id)
    {
        var alert = Assert.Single(await CreateGuardService(mode).GetSystemAlertsAsync(TestContext.Current.CancellationToken),a => a.Id.StartsWith("snapshot-guard-", StringComparison.Ordinal));

        Assert.Equal(id, alert.Id);
        Assert.Equal("warning", alert.Type);
        Assert.Equal("/admin/security/settings", alert.LinkUrl);
        Assert.Equal("Change", alert.LinkText);
    }

    [Fact]
    public async Task Enforce_RaisesNoModeAlert()
    {
        Assert.DoesNotContain(await CreateGuardService("Enforce").GetSystemAlertsAsync(TestContext.Current.CancellationToken),a => a.Id.StartsWith("snapshot-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RefusalsInTheLastSevenDays_AreCounted_KeyedByTheNewestEventInTheWindow()
    {
        AddSecurityEvent(SecurityEventKind.SnapshotRejected, "u-1", DateTime.UtcNow.AddDays(-1), count: 2);
        long u2Id = AddSecurityEvent(SecurityEventKind.SnapshotRejected, "u-2", DateTime.UtcNow.AddHours(-1));
        AddSecurityEvent(SecurityEventKind.SnapshotRejected, "u-3", DateTime.UtcNow.AddDays(-8));

        var alert = Assert.Single(await CreateGuardService("Enforce").GetSystemAlertsAsync(TestContext.Current.CancellationToken), a => a.Id.StartsWith("snapshot-refusals:", StringComparison.Ordinal));

        Assert.Equal($"snapshot-refusals:{u2Id}", alert.Id);
        Assert.Equal("warning", alert.Type);
        Assert.Equal("3 game snapshots were refused for text addressed to the AI in the last 7 days (2 users).", alert.Message);
        Assert.Equal("/admin/security/events", alert.LinkUrl);
        Assert.Equal("Review", alert.LinkText);
    }

    [Fact]
    public async Task ARepeatFoldedIntoTheSameEvent_KeepsTheAlertId()
    {
        long eventId = AddSecurityEvent(SecurityEventKind.SnapshotRejected, "u-1", DateTime.UtcNow.AddMinutes(-5));
        var service = CreateGuardService("Enforce");
        var before = Assert.Single(await service.GetSystemAlertsAsync(TestContext.Current.CancellationToken), a => a.Id.StartsWith("snapshot-refusals:", StringComparison.Ordinal));

        using (var db = new ApplicationDbContext(_dbOptions))
        {
            var securityEvent = db.SecurityEvents.Single(e => e.Id == eventId);
            securityEvent.Count = 3;
            securityEvent.LastOccurredUtc = DateTime.UtcNow;
            db.SaveChanges();
        }

        var after = Assert.Single(await service.GetSystemAlertsAsync(TestContext.Current.CancellationToken), a => a.Id.StartsWith("snapshot-refusals:", StringComparison.Ordinal));
        Assert.Equal(before.Id, after.Id);
        Assert.Equal($"snapshot-refusals:{eventId}", after.Id);
        Assert.Equal("3 game snapshots were refused for text addressed to the AI in the last 7 days (1 user).", after.Message);
    }

    [Fact]
    public async Task ANewRefusalEvent_ChangesTheAlertId()
    {
        AddSecurityEvent(SecurityEventKind.SnapshotRejected, "u-1", DateTime.UtcNow.AddHours(-2));
        var service = CreateGuardService("Enforce");
        var before = Assert.Single(await service.GetSystemAlertsAsync(TestContext.Current.CancellationToken), a => a.Id.StartsWith("snapshot-refusals:", StringComparison.Ordinal));

        long newId = AddSecurityEvent(SecurityEventKind.SnapshotRejected, "u-1", DateTime.UtcNow.AddMinutes(-1));

        var after = Assert.Single(await service.GetSystemAlertsAsync(TestContext.Current.CancellationToken), a => a.Id.StartsWith("snapshot-refusals:", StringComparison.Ordinal));
        Assert.NotEqual(before.Id, after.Id);
        Assert.Equal($"snapshot-refusals:{newId}", after.Id);
    }

    [Fact]
    public async Task AnAutomaticBlock_IsAnErrorAlert_KeyedByTheNewestEvent()
    {
        AddSecurityEvent(SecurityEventKind.AutoBlocked, "u-1", DateTime.UtcNow.AddDays(-2));
        long newest;
        using (var db = new ApplicationDbContext(_dbOptions))
        {
            newest = db.SecurityEvents.Max(e => e.Id);
        }

        var alerts = (await CreateGuardService("Enforce").GetSystemAlertsAsync(TestContext.Current.CancellationToken)).ToList();

        var alert = Assert.Single(alerts, a => a.Id.StartsWith("snapshot-autoblock:", StringComparison.Ordinal));
        Assert.Equal($"snapshot-autoblock:{newest}", alert.Id);
        Assert.Equal("error", alert.Type);
        Assert.Equal("1 user was suspended from the AI chat automatically for repeated game text addressed to the AI.", alert.Message);
        Assert.Equal("/admin/security/events", alert.LinkUrl);
        Assert.DoesNotContain(alerts, a => a.Id.StartsWith("snapshot-refusals:", StringComparison.Ordinal));
    }

    // -- Shared tasks ---------------------------------------------------------------------------------

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<SystemAlert?> GuardAlert(string prefix) =>
        (await CreateGuardService("Enforce").GetSystemAlertsAsync(Ct)).SingleOrDefault(a => a.Id.StartsWith(prefix, StringComparison.Ordinal));

    private AdminAlertHandling? Handling(string key)
    {
        using var db = new ApplicationDbContext(_dbOptions);
        return db.AdminAlertHandlings.SingleOrDefault(h => h.TaskKey == key);
    }

    [Fact]
    public async Task EventReviews_AreSharedTasks_AndConditionsAndDecisionsAreNot()
    {
        AddSecurityEvent(SecurityEventKind.SnapshotRejected, "u-1", DateTime.UtcNow.AddHours(-1));
        AddSecurityEvent(SecurityEventKind.AutoBlocked, "u-1", DateTime.UtcNow.AddHours(-1));
        AddSpend("spender-1", "spender-one", (1, 30m, 1));

        var guardAlerts = await CreateGuardService("Observe").GetSystemAlertsAsync(Ct);
        Assert.True(Assert.Single(guardAlerts, a => a.Id.StartsWith("snapshot-refusals:", StringComparison.Ordinal)).IsSharedTask);
        Assert.True(Assert.Single(guardAlerts, a => a.Id.StartsWith("snapshot-autoblock:", StringComparison.Ordinal)).IsSharedTask);
        Assert.False(Assert.Single(guardAlerts, a => a.Id == "snapshot-guard-observe").IsSharedTask);
        Assert.False(Assert.Single(guardAlerts, a => a.Id == "sentry-dsn-missing").IsSharedTask);

        Assert.True(Assert.Single(await SpendAlerts(20m)).IsSharedTask);
        Assert.False(AdminSharedTask.TryParse("account-deletion-review:5", out _));
    }

    [Fact]
    public async Task MarkingHandled_ClearsTheAlert_OnANewServiceInstance()
    {
        long eventId = AddSecurityEvent(SecurityEventKind.SnapshotRejected, "u-1", DateTime.UtcNow.AddHours(-1));

        Assert.True(await CreateGuardService("Enforce").MarkAlertHandledAsync($"snapshot-refusals:{eventId}", "admin-1", Ct));

        Assert.Null(await GuardAlert("snapshot-refusals:"));
        var handling = Handling("snapshot-refusals")!;
        Assert.Equal(eventId, handling.HandledThrough);
        Assert.Equal("admin-1", handling.HandledByUserId);
    }

    [Fact]
    public async Task ANewerRefusal_RaisesAHandledAlertAgain()
    {
        long eventId = AddSecurityEvent(SecurityEventKind.SnapshotRejected, "u-1", DateTime.UtcNow.AddHours(-2));
        await CreateGuardService("Enforce").MarkAlertHandledAsync($"snapshot-refusals:{eventId}", "admin-1", Ct);

        long newId = AddSecurityEvent(SecurityEventKind.SnapshotRejected, "u-2", DateTime.UtcNow.AddMinutes(-1));

        Assert.Equal($"snapshot-refusals:{newId}", (await GuardAlert("snapshot-refusals:"))!.Id);
    }

    [Fact]
    public async Task AFoldedRepeat_DoesNotRaiseAHandledAlertAgain()
    {
        long eventId = AddSecurityEvent(SecurityEventKind.SnapshotRejected, "u-1", DateTime.UtcNow.AddMinutes(-5));
        await CreateGuardService("Enforce").MarkAlertHandledAsync($"snapshot-refusals:{eventId}", "admin-1", Ct);

        using (var db = new ApplicationDbContext(_dbOptions))
        {
            var securityEvent = db.SecurityEvents.Single(e => e.Id == eventId);
            securityEvent.Count = 3;
            securityEvent.LastOccurredUtc = DateTime.UtcNow;
            db.SaveChanges();
        }

        Assert.Null(await GuardAlert("snapshot-refusals:"));
    }

    [Fact]
    public async Task HandledRefusals_DoNotHideAnAutomaticBlock()
    {
        long refusal = AddSecurityEvent(SecurityEventKind.SnapshotRejected, "u-1", DateTime.UtcNow.AddHours(-2));
        AddSecurityEvent(SecurityEventKind.AutoBlocked, "u-1", DateTime.UtcNow.AddHours(-3));
        await CreateGuardService("Enforce").MarkAlertHandledAsync($"snapshot-refusals:{refusal + 100}", "admin-1", Ct);

        Assert.Null(await GuardAlert("snapshot-refusals:"));
        Assert.NotNull(await GuardAlert("snapshot-autoblock:"));
    }

    [Fact]
    public async Task HandledAiSpend_StaysHiddenAtALowerCount_AndReturnsAtAHigherCountOrOnANewDate()
    {
        AddSpend("spender-1", "spender-one", (1, 30m, 1));
        AddSpend("spender-2", "spender-two", (1, 30m, 1));
        var service = CreateSpendService(20m);
        var dateKey = Assert.Single(await SpendAlerts(20m)).Id[..^2];

        await service.MarkAlertHandledAsync(dateKey + ":3", "admin-1", Ct);
        Assert.Empty(await SpendAlerts(20m));

        AddSpend("spender-3", "spender-three", (1, 30m, 1));
        Assert.Empty(await SpendAlerts(20m));

        AddSpend("spender-4", "spender-four", (1, 30m, 1));
        Assert.Equal(dateKey + ":4", Assert.Single(await SpendAlerts(20m)).Id);

        await service.MarkAlertHandledAsync(dateKey + ":4", "admin-1", Ct);
        Assert.Empty(await SpendAlerts(20m));

        using (var db = new ApplicationDbContext(_dbOptions))
        {
            var handling = db.AdminAlertHandlings.Single();
            db.AdminAlertHandlings.Remove(handling);
            db.AdminAlertHandlings.Add(new AdminAlertHandling { TaskKey = "ai-spend:20000101", HandledThrough = 99, HandledAtUtc = DateTime.UtcNow });
            db.SaveChanges();
        }
        Assert.Single(await SpendAlerts(20m));
    }

    [Fact]
    public async Task MarkingALowerSequence_NeverLowersTheMark_AndKeepsTheFirstHandler()
    {
        var service = CreateGuardService("Enforce");
        Assert.True(await service.MarkAlertHandledAsync("snapshot-refusals:10", "admin-1", Ct));
        var first = Handling("snapshot-refusals")!;

        Assert.True(await service.MarkAlertHandledAsync("snapshot-refusals:4", "admin-2", Ct));

        var after = Handling("snapshot-refusals")!;
        Assert.Equal(10, after.HandledThrough);
        Assert.Equal("admin-1", after.HandledByUserId);
        Assert.Equal(first.HandledAtUtc, after.HandledAtUtc);
    }

    [Fact]
    public async Task MarkingAHigherSequence_RaisesTheMark_AndRecordsTheHandler()
    {
        var service = CreateGuardService("Enforce");
        await service.MarkAlertHandledAsync("snapshot-refusals:10", "admin-1", Ct);

        await service.MarkAlertHandledAsync("snapshot-refusals:12", "admin-2", Ct);

        var after = Handling("snapshot-refusals")!;
        Assert.Equal(12, after.HandledThrough);
        Assert.Equal("admin-2", after.HandledByUserId);
    }

    [Fact]
    public async Task MarkingTheSameIdTwice_Succeeds_AndLeavesOneRow()
    {
        var service = CreateGuardService("Enforce");

        Assert.True(await service.MarkAlertHandledAsync("snapshot-autoblock:3", "admin-1", Ct));
        Assert.True(await service.MarkAlertHandledAsync("snapshot-autoblock:3", "admin-2", Ct));

        using var db = new ApplicationDbContext(_dbOptions);
        var handling = Assert.Single(db.AdminAlertHandlings);
        Assert.Equal("admin-1", handling.HandledByUserId);
    }

    [Theory]
    [InlineData("sentry-dsn-missing")]
    [InlineData("account-deletion-review:5")]
    [InlineData(null)]
    public async Task MarkingAnythingButASharedTask_ReturnsFalse_AndWritesNothing(string? alertId)
    {
        Assert.False(await CreateGuardService("Enforce").MarkAlertHandledAsync(alertId, "admin-1", Ct));

        using var db = new ApplicationDbContext(_dbOptions);
        Assert.Empty(db.AdminAlertHandlings);
    }

    [Fact]
    public async Task AFailingHandlingLookup_KeepsTheAlertsVisible()
    {
        var failingOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new FailingHandlingLookup())
            .Options;
        using (var db = new ApplicationDbContext(failingOptions))
        {
            var securityEvent = new SecurityEvent
            {
                Kind = SecurityEventKind.SnapshotRejected,
                AspNetUserId = "u-1",
                OccurredUtc = DateTime.UtcNow.AddHours(-1),
                LastOccurredUtc = DateTime.UtcNow.AddHours(-1),
                Count = 1,
                Code = "injection-own"
            };
            db.SecurityEvents.Add(securityEvent);
            db.AdminAlertHandlings.Add(new AdminAlertHandling { TaskKey = "snapshot-refusals", HandledThrough = long.MaxValue, HandledAtUtc = DateTime.UtcNow });
            db.SaveChanges();
        }

        var services = new ServiceCollection();
        services.AddScoped(_ => new ApplicationDbContext(failingOptions));
        var configuration = new ConfigurationBuilder().Build();
        var service = new ConfigHealthService(configuration, services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>());

        var alert = Assert.Single(await service.GetSystemAlertsAsync(Ct), a => a.Id.StartsWith("snapshot-refusals:", StringComparison.Ordinal));
        Assert.True(alert.IsSharedTask);
    }

    /// <summary>Fails every query that reads the handling table; the other queries run.</summary>
    private sealed class FailingHandlingLookup : Microsoft.EntityFrameworkCore.Diagnostics.IQueryExpressionInterceptor
    {
        public System.Linq.Expressions.Expression QueryCompilationStarting(
            System.Linq.Expressions.Expression queryExpression,
            Microsoft.EntityFrameworkCore.Diagnostics.QueryExpressionEventData eventData)
            => typeof(IQueryable<AdminAlertHandling>).IsAssignableFrom(queryExpression.Type)
                ? throw new InvalidOperationException("The handling table cannot be read.")
                : queryExpression;
    }
}
