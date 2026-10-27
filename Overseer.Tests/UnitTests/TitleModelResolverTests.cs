using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MobileGnollHackLogger.Data;
using Overseer.Services;
using Overseer.Services.Privacy;
using Overseer.Services.Providers;
using Xunit;

namespace Overseer.Tests.UnitTests;

/// <summary>
/// The title model resolution order -- selected system configuration, selected user model, then Default
/// (First Available) -- and the fallback past every model that cannot be used.
/// </summary>
public class TitleModelResolverTests
{
    private const string UserId = "title-user";
    private const string SystemKey = "test-system-key";
    private const string UserKey = "test-user-key";

    private readonly ApplicationDbContext _db;
    private readonly CryptoService _crypto;
    private readonly TitleModelResolver _resolver;

    public TitleModelResolverTests()
    {
        _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        var keyBytes = new byte[32];
        keyBytes[0] = 77;
        keyBytes[31] = 99;
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { { "AesEncryptionKey", Convert.ToBase64String(keyBytes) } })
            .Build();

        _crypto = new CryptoService(config);
        var settingsService = new SettingsService(_db, _crypto, new ConfidentialityPostureService(config),
            new ModelAvailabilityService(new ModelMetadataService()));
        var providers = new IAiProvider[]
        {
            new OpenAiResponsesProvider(config), new AnthropicProvider(config), new GoogleProvider(config)
        };
        _resolver = new TitleModelResolver(_db, new SystemAiConfigService(_db, NullLogger<SystemAiConfigService>.Instance),
            settingsService, _crypto, new EndpointPolicy(config), providers);
    }

    /// <summary>An enabled, system-wide configuration with a key, its counters reset today.</summary>
    private SystemAiApiConfiguration AddConfig(string name, int orderIndex, int modelRole = 3, bool withKey = true)
    {
        var config = new SystemAiApiConfiguration
        {
            DisplayName = name,
            Provider = "OpenAI",
            ModelId = "gpt-" + name.ToLowerInvariant(),
            IsEnabled = true,
            IsSystemWide = true,
            ModelRole = modelRole,
            OrderIndex = orderIndex,
            LastDailyReset = DateTime.UtcNow,
            LastMonthlyReset = DateTime.UtcNow
        };
        if (withKey)
        {
            var (ciphertext, nonce, tag) = _crypto.Encrypt(SystemKey, "SYSTEM_API_KEY");
            config.EncryptedApiKey = ciphertext;
            config.ApiKeyNonce = nonce;
            config.ApiKeyTag = tag;
        }
        _db.SystemAiApiConfigurations.Add(config);
        return config;
    }

    private UserAiModel AddUserModel(string provider, string modelId, int orderIndex)
    {
        var model = new UserAiModel { AspNetUserId = UserId, Provider = provider, ModelId = modelId, OrderIndex = orderIndex };
        _db.UserAiModels.Add(model);
        return model;
    }

    private void AddUserKey(string provider)
    {
        var (ciphertext, nonce, tag) = _crypto.Encrypt(UserKey, UserId);
        _db.UserAiApiKeys.Add(new UserAiApiKey
        {
            AspNetUserId = UserId, Provider = provider, EncryptedApiKey = ciphertext, ApiKeyNonce = nonce, ApiKeyTag = tag
        });
    }

    private async Task<TitleModelResolution> ResolveAsync(long? systemModelId = null, long? userModelId = null)
    {
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var settings = new UserAiSettings
        {
            AspNetUserId = UserId,
            TitleGenerationSystemModelId = systemModelId,
            TitleGenerationModelId = userModelId
        };
        return await _resolver.ResolveAsync(settings, UserId, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SelectedSystemConfig_WhenUsable_IsChosenWithItsLevelAndMode()
    {
        var selected = AddConfig("Title", 0);
        selected.ThinkingLevel = "low";
        selected.ReasoningMode = "pro";
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var resolution = await ResolveAsync(systemModelId: selected.Id);

        var choice = Assert.IsType<TitleModelChoice>(resolution.Choice);
        Assert.Equal(selected.Id, choice.SystemConfig?.Id);
        Assert.Null(choice.UserModel);
        Assert.Equal("low", choice.ThinkingLevel);
        Assert.Equal("pro", choice.ReasoningMode);
        Assert.Equal(SystemKey, choice.ApiKey);
        Assert.Empty(resolution.SkipReasons);
        Assert.False(resolution.IsFallback);
    }

    [Fact]
    public async Task SelectedSystemConfig_WhenDeleted_FallsToTheUsersFirstKeyedModel()
    {
        var userModel = AddUserModel("OpenAI", "gpt-own", 0);
        AddUserKey("OpenAI");
        AddConfig("Other", 0);

        var resolution = await ResolveAsync(systemModelId: 999);

        var choice = Assert.IsType<TitleModelChoice>(resolution.Choice);
        Assert.Equal(userModel.Id, choice.UserModel?.Id);
        Assert.Null(choice.SystemConfig);
        Assert.Equal(UserKey, choice.ApiKey);
        var reason = Assert.Single(resolution.SkipReasons);
        Assert.Contains("System AI Config 999", reason);
        Assert.True(resolution.IsFallback);
    }

    [Fact]
    public async Task SelectedSystemConfig_WhenDisabled_FallsToTheNextTitleConfig()
    {
        var selected = AddConfig("Disabled", 0);
        selected.IsEnabled = false;
        var next = AddConfig("Next", 1);

        var resolution = await ResolveAsync(systemModelId: selected.Id);

        Assert.Equal(next.Id, resolution.Choice?.SystemConfig?.Id);
        Assert.True(resolution.IsFallback);
    }

    [Fact]
    public async Task SelectedSystemConfig_WithoutAKey_FallsThrough()
    {
        var selected = AddConfig("Keyless", 0, withKey: false);
        var next = AddConfig("Next", 1);

        var resolution = await ResolveAsync(systemModelId: selected.Id);

        Assert.Equal(next.Id, resolution.Choice?.SystemConfig?.Id);
        Assert.Contains(resolution.SkipReasons, r => r.Contains("no API key"));
    }

    [Fact]
    public async Task SelectedSystemConfig_OverItsDailyTitleLimit_FallsToTheNextAllowedConfig()
    {
        var selected = AddConfig("Exhausted", 0);
        selected.MaxDailyTitleRequests = 1;
        selected.DailyTitleRequestsCount = 1;
        var next = AddConfig("Next", 1);

        var resolution = await ResolveAsync(systemModelId: selected.Id);

        Assert.Equal(next.Id, resolution.Choice?.SystemConfig?.Id);
        Assert.Single(resolution.SkipReasons);
    }

    [Fact]
    public async Task SelectedUserModel_WhenDeleted_FallsToDefault()
    {
        var userModel = AddUserModel("OpenAI", "gpt-own", 0);
        AddUserKey("OpenAI");

        var resolution = await ResolveAsync(userModelId: 999);

        Assert.Equal(userModel.Id, resolution.Choice?.UserModel?.Id);
        Assert.Single(resolution.SkipReasons);
        Assert.True(resolution.IsFallback);
    }

    [Fact]
    public async Task Default_SkipsAUserModelWhoseProviderHasNoKey()
    {
        AddUserModel("Anthropic", "claude-own", 0);
        var keyed = AddUserModel("OpenAI", "gpt-own", 1);
        AddUserKey("OpenAI");

        var resolution = await ResolveAsync();

        Assert.Equal(keyed.Id, resolution.Choice?.UserModel?.Id);
        Assert.False(resolution.IsFallback);
    }

    [Fact]
    public async Task Default_TriesEveryTitleConfig_UntilOneIsAllowed()
    {
        var refused = AddConfig("Exhausted", 0);
        refused.MaxDailyTitleRequests = 1;
        refused.DailyTitleRequestsCount = 1;
        var allowed = AddConfig("Allowed", 1);

        var resolution = await ResolveAsync();

        Assert.Equal(allowed.Id, resolution.Choice?.SystemConfig?.Id);
        Assert.Single(resolution.SkipReasons);
    }

    [Fact]
    public async Task Default_WithNoTitleRoleConfig_UsesAChatConfigCheckedAsChat()
    {
        var chatOnly = AddConfig("Chat", 0, modelRole: 1);

        var resolution = await ResolveAsync();

        Assert.Equal(chatOnly.Id, resolution.Choice?.SystemConfig?.Id);
    }

    [Fact]
    public async Task NothingUsable_ReturnsNoChoice_WithTheReasons()
    {
        AddUserModel("OpenAI", "gpt-own", 0);
        AddConfig("Keyless", 0, withKey: false);

        var resolution = await ResolveAsync(systemModelId: 999);

        Assert.Null(resolution.Choice);
        Assert.Equal(3, resolution.SkipReasons.Count);
    }
}
