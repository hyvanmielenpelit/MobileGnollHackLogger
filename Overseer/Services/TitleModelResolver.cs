using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Services.Providers;

namespace Overseer.Services;

/// <summary>The model a chat title is generated with, and the key, endpoint and reasoning settings it runs on.</summary>
/// <param name="SystemConfig">The system configuration that funds the title; null on the user's own key.</param>
/// <param name="UserModel">The user's own model the title runs on; null on a system configuration.</param>
public sealed record TitleModelChoice(
    string Provider, string ModelId, string ApiKey, string? ServiceTier,
    string? ThinkingLevel, string? ReasoningMode, AiEndpointDescriptor Endpoint,
    SystemAiApiConfiguration? SystemConfig, UserAiModel? UserModel);

/// <summary>The chosen title model, or null when none is usable, and why each model tried before it was skipped.</summary>
public sealed record TitleModelResolution(TitleModelChoice? Choice, IReadOnlyList<string> SkipReasons)
{
    /// <summary>Whether the user's selected title model was skipped for another one.</summary>
    public bool IsFallback { get; init; }
}

/// <summary>
/// Resolves the title model: the user's selected system configuration, then the selected user model,
/// then Default (First Available) -- the user's own models in order, then the system models. The first
/// usable one wins: allowed by <see cref="SystemAiConfigService.GetAndCheckSystemConfigAsync"/> where it
/// applies, holding a complete encrypted key, on a supported provider. A selection that is refused for any
/// reason falls through to Default.
/// </summary>
public class TitleModelResolver
{
    private const int TitleRole = 2;
    private const int ChatRole = 1;

    private readonly ApplicationDbContext _dbContext;
    private readonly SystemAiConfigService _systemAiConfigService;
    private readonly SettingsService _settingsService;
    private readonly CryptoService _cryptoService;
    private readonly Overseer.Services.Privacy.EndpointPolicy _endpointPolicy;
    private readonly HashSet<string> _supportedProviders;

    public TitleModelResolver(
        ApplicationDbContext dbContext,
        SystemAiConfigService systemAiConfigService,
        SettingsService settingsService,
        CryptoService cryptoService,
        Overseer.Services.Privacy.EndpointPolicy endpointPolicy,
        IEnumerable<IAiProvider> aiProviders)
    {
        _dbContext = dbContext;
        _systemAiConfigService = systemAiConfigService;
        _settingsService = settingsService;
        _cryptoService = cryptoService;
        _endpointPolicy = endpointPolicy;
        _supportedProviders = new HashSet<string>(aiProviders.Select(p => p.ProviderName), StringComparer.OrdinalIgnoreCase);
    }

    public async Task<TitleModelResolution> ResolveAsync(UserAiSettings? settings, string userId, CancellationToken ct)
    {
        var reasons = new List<string>();

        if (settings?.TitleGenerationSystemModelId is long selectedConfigId)
        {
            var (choice, reason) = await TrySystemConfigAsync(selectedConfigId, userId, TitleRole);
            if (choice != null)
            {
                return new TitleModelResolution(choice, reasons);
            }
            reasons.Add($"Selected title model (System AI Config {selectedConfigId}) is unavailable: {reason}");
        }
        else if (settings?.TitleGenerationModelId is long selectedModelId)
        {
            var selectedModel = await _dbContext.UserAiModels
                .FirstOrDefaultAsync(m => m.Id == selectedModelId && m.AspNetUserId == userId, ct);
            if (selectedModel == null)
            {
                reasons.Add($"Selected title model (user model {selectedModelId}) is unavailable: the model no longer exists.");
            }
            else
            {
                var (choice, reason) = await TryUserModelAsync(selectedModel, userId, ct);
                if (choice != null)
                {
                    return new TitleModelResolution(choice, reasons);
                }
                reasons.Add($"Selected title model ({selectedModel.Provider}/{selectedModel.ModelId}) is unavailable: {reason}");
            }
        }

        bool isFallback = reasons.Count > 0;

        var userModels = await _dbContext.UserAiModels
            .Where(m => m.AspNetUserId == userId)
            .OrderBy(m => m.OrderIndex)
            .ToListAsync(ct);
        foreach (var userModel in userModels)
        {
            if (settings?.TitleGenerationSystemModelId == null && settings?.TitleGenerationModelId == userModel.Id)
            {
                // The selected model, already refused above.
                continue;
            }

            var (choice, reason) = await TryUserModelAsync(userModel, userId, ct);
            if (choice != null)
            {
                return new TitleModelResolution(choice, reasons) { IsFallback = isFallback };
            }
            reasons.Add($"{userModel.Provider}/{userModel.ModelId} skipped: {reason}");
        }

        /* Checked against the role the list was built with: a role-1 fallback would be refused by
           a fixed role-2 check. */
        int systemRole = TitleRole;
        var systemModels = await _settingsService.GetResolvedSystemModelsAsync(userId, roleFilter: TitleRole);
        if (systemModels.Count == 0)
        {
            systemRole = ChatRole;
            systemModels = await _settingsService.GetResolvedSystemModelsAsync(userId, roleFilter: ChatRole);
        }

        foreach (var (config, _) in systemModels)
        {
            if (settings?.TitleGenerationSystemModelId == config.Id && systemRole == TitleRole)
            {
                // The selected configuration, already refused above with the same role.
                continue;
            }

            var (choice, reason) = await TrySystemConfigAsync(config.Id, userId, systemRole);
            if (choice != null)
            {
                return new TitleModelResolution(choice, reasons) { IsFallback = isFallback };
            }
            reasons.Add($"System AI Config {config.Id} ({config.Provider}/{config.ModelId}) skipped: {reason}");
        }

        return new TitleModelResolution(null, reasons) { IsFallback = isFallback };
    }

    private async Task<(TitleModelChoice? Choice, string? Reason)> TrySystemConfigAsync(long configId, string userId, int role)
    {
        var (config, refusal) = await _systemAiConfigService.GetAndCheckSystemConfigAsync(configId, userId, requiredRoleFilter: role);
        if (config == null)
        {
            return (null, refusal);
        }
        if (string.IsNullOrEmpty(config.EncryptedApiKey) || string.IsNullOrEmpty(config.ApiKeyNonce) || string.IsNullOrEmpty(config.ApiKeyTag))
        {
            return (null, "it has no API key.");
        }
        if (!_supportedProviders.Contains(config.Provider))
        {
            return (null, $"provider {config.Provider} is not supported.");
        }

        string apiKey = _cryptoService.Decrypt(config.EncryptedApiKey, config.ApiKeyNonce, config.ApiKeyTag, "SYSTEM_API_KEY");
        return (new TitleModelChoice(
            config.Provider, config.ModelId, apiKey, config.ServiceTier,
            config.ThinkingLevel, config.ReasoningMode, _endpointPolicy.Resolve(config),
            SystemConfig: config, UserModel: null), null);
    }

    private async Task<(TitleModelChoice? Choice, string? Reason)> TryUserModelAsync(UserAiModel model, string userId, CancellationToken ct)
    {
        if (!_supportedProviders.Contains(model.Provider))
        {
            return (null, $"provider {model.Provider} is not supported.");
        }

        var apiKeyEntry = await _dbContext.UserAiApiKeys
            .FirstOrDefaultAsync(k => k.AspNetUserId == userId && k.Provider == model.Provider, ct);
        if (apiKeyEntry == null || string.IsNullOrEmpty(apiKeyEntry.EncryptedApiKey) || string.IsNullOrEmpty(apiKeyEntry.ApiKeyNonce) || string.IsNullOrEmpty(apiKeyEntry.ApiKeyTag))
        {
            return (null, $"no API key for {model.Provider}.");
        }

        string apiKey = _cryptoService.Decrypt(apiKeyEntry.EncryptedApiKey, apiKeyEntry.ApiKeyNonce, apiKeyEntry.ApiKeyTag, userId);
        return (new TitleModelChoice(
            model.Provider, model.ModelId, apiKey, model.ServiceTier,
            model.ThinkingLevel, model.ReasoningMode, _endpointPolicy.Resolve(apiKeyEntry),
            SystemConfig: null, UserModel: model), null);
    }
}
