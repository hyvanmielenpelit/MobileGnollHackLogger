using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MobileGnollHackLogger.Data;
using Overseer.Models;
using Overseer.Services;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The retired-model alert: one per retired catalog entry that enabled system configurations still
/// name, and none for a configuration that is disabled, custom, on a custom endpoint or simply not in
/// the catalog.
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
        Assert.Equal("/admin?tab=configs", alert.LinkUrl);
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

    [Fact]
    public void AlertsWithoutALink_LeaveTheLinkFieldsNull()
    {
        var alert = Assert.Single(new ConfigHealthService(new ConfigurationBuilder().Build())
            .GetSystemAlerts()
            .Where(a => a.Id == "sentry-dsn-missing"));

        Assert.Null(alert.LinkUrl);
        Assert.Null(alert.LinkText);
    }
}
