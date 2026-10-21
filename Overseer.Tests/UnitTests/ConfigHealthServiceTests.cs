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

    private List<SystemAlert> RetiredAlerts() =>
        CreateService().GetSystemAlerts().Where(a => a.Id.StartsWith("retired-model-", StringComparison.Ordinal)).ToList();

    [Fact]
    public void TwoEnabledRetiredConfigurations_RaiseOneGroupedAlert_WithALink()
    {
        AddConfigs(
            Config("Flash", RetiredModelId),
            Config("Flash snapshot", RetiredModelId + "-001"),
            Config("Disabled flash", RetiredModelId, isEnabled: false),
            Config("Custom flash", RetiredModelId, catalogMode: ModelCatalogModes.Custom),
            Config("Gateway flash", RetiredModelId, baseUrl: "https://llm.example.com/v1"));

        var alert = Assert.Single(RetiredAlerts());

        Assert.Equal("retired-model-Google-gemini-3.7-flash", alert.Id);
        Assert.Equal("warning", alert.Type);
        Assert.Equal(
            "2 system configurations use Gemini 3.7 Flash (gemini-3.7-flash), which was removed from the model catalog on 2026-10-10.",
            alert.Message);
        Assert.Equal("/admin/configs", alert.LinkUrl);
        Assert.Equal("Review in System Configs", alert.LinkText);
    }

    [Fact]
    public void OneRetiredConfiguration_IsCountedInTheSingular()
    {
        AddConfigs(Config("Flash", RetiredModelId, catalogMode: null));

        var alert = Assert.Single(RetiredAlerts());

        Assert.StartsWith("1 system configuration uses Gemini 3.7 Flash (gemini-3.7-flash)", alert.Message);
    }

    [Fact]
    public void DisabledCustomEndpointAndNotInCatalogConfigurations_RaiseNoAlert()
    {
        AddConfigs(
            Config("Disabled flash", RetiredModelId, isEnabled: false),
            Config("Custom flash", RetiredModelId, catalogMode: ModelCatalogModes.Custom),
            Config("Gateway flash", RetiredModelId, baseUrl: "https://llm.example.com/v1"),
            Config("Tuned flash", "my-tuned-flash"));

        var alerts = CreateService().GetSystemAlerts().ToList();

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

    private List<SystemAlert> SpendAlerts(decimal alertThreshold) =>
        CreateSpendService(alertThreshold).GetSystemAlerts().Where(a => a.Id.StartsWith("ai-spend:", StringComparison.Ordinal)).ToList();

    [Fact]
    public void AiSpend_AtTheThreshold_RaisesOneAlert_WithTheDateAndCountInItsId()
    {
        AddSpend("spender-1", "spender-one", (1, 15m, 1), (3, 5m, 20));
        AddSpend("spender-2", "spender-two", (2, 30m, 2));
        // Below the threshold once GnollBench and older rows are left out.
        AddSpend("spender-3", "spender-three", (1, 10m, 1), (4, 100m, 1), (1, 100m, 25));

        var before = DateTime.UtcNow;
        var alert = Assert.Single(SpendAlerts(20m));
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
    public void AiSpend_OneUser_IsCountedInTheSingular()
    {
        AddSpend("spender-1", "spender-one", (1, 20m, 1));

        var alert = Assert.Single(SpendAlerts(20m));

        Assert.EndsWith(":1", alert.Id, StringComparison.Ordinal);
        Assert.StartsWith("1 user spent over $20.00", alert.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AiSpend_BelowTheThreshold_RaisesNoAlert()
    {
        AddSpend("spender-1", "spender-one", (1, 19.99m, 1), (1, null, 1));

        Assert.Empty(SpendAlerts(20m));
    }

    [Fact]
    public void AiSpend_ByAnAdministrator_RaisesNoAlert()
    {
        AddSpend("boss", "spend-boss", (1, 500m, 1));

        Assert.Empty(SpendAlerts(20m));
    }

    [Fact]
    public void AiSpend_WithTheAlertTurnedOff_RaisesNoAlert()
    {
        AddSpend("spender-1", "spender-one", (1, 500m, 1));

        Assert.Empty(SpendAlerts(0m));
    }

    [Fact]
    public void AlertsWithoutALink_LeaveTheLinkFieldsNull()
    {
        var alert = Assert.Single(new ConfigHealthService(new ConfigurationBuilder().Build())
            .GetSystemAlerts(), a => a.Id == "sentry-dsn-missing");

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
    public void SourceCodePathMissing_RaisesAWarning(string? path)
    {
        var alert = Assert.Single(new ConfigHealthService(ConfigurationWith(("SourceCodePath", path))).GetSystemAlerts(),
            a => a.Id == "source-code-path-missing");

        Assert.Equal("warning", alert.Type);
        Assert.Equal("GnollHack source code path is not configured. Set SourceCodePath in configuration settings.", alert.Message);
        Assert.Null(alert.LinkUrl);
    }

    [Fact]
    public void SourceCodePathConfigured_RaisesNoAlert()
    {
        var alerts = new ConfigHealthService(ConfigurationWith(("SourceCodePath", @"C:\repos\GnollHack"))).GetSystemAlerts();

        Assert.DoesNotContain(alerts, a => a.Id == "source-code-path-missing");
    }

    [Theory]
    [InlineData(MakedefsOutcomeKind.BuildFailed)]
    [InlineData(MakedefsOutcomeKind.RunFailed)]
    public void MakedefsFailed_WithAMissingHeader_RaisesAnErrorWithALink(MakedefsOutcomeKind kind)
    {
        var service = new MakedefsStateHealthService(ConfigurationWith(), MakedefsState(kind, "pm.h"));

        var alert = Assert.Single(service.GetSystemAlerts(), a => a.Id == "gnollhack-makedefs-failed");

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
    public void MakedefsAlert_IsAbsent_UnlessAFailureLeavesAHeaderMissing(MakedefsOutcomeKind? kind, bool headerMissing)
    {
        var state = headerMissing ? MakedefsState(kind, "onames.h") : MakedefsState(kind);
        var service = new MakedefsStateHealthService(ConfigurationWith(), state);

        Assert.DoesNotContain(service.GetSystemAlerts(), a => a.Id == "gnollhack-makedefs-failed");
    }

    [Fact]
    public void MakedefsAlert_IsAbsent_WhenTheSourceServiceIsUnavailable()
    {
        Assert.DoesNotContain(new MakedefsStateHealthService(ConfigurationWith(), null).GetSystemAlerts(),
            a => a.Id == "gnollhack-makedefs-failed");
        Assert.DoesNotContain(CreateService().GetSystemAlerts(), a => a.Id == "gnollhack-makedefs-failed");
    }
}
