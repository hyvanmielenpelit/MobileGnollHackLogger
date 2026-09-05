using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using System.Text.Json;
using ParallelExecutionMode = MobileGnollHackLogger.Data.ParallelExecutionMode;

namespace Overseer.Services;

public class SettingsService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly CryptoService _cryptoService;

    public SettingsService(ApplicationDbContext dbContext, CryptoService cryptoService)
    {
        _dbContext = dbContext;
        _cryptoService = cryptoService;
    }

    public async Task<UserAiSettings?> GetSettingsAsync(string userId)
    {
        return await _dbContext.UserAiSettings.FindAsync(userId);
    }

    public static readonly string[] SupportedProviders = new[] { "OpenAI", "Anthropic", "Google" };

    public async Task SaveSettingsAsync(string userId, bool? spoilerFreeMode = null, bool? enableWebSearch = null, bool? enableToolUse = null, bool? enableClientTools = null, bool? enableGameActions = null, bool? showSourceCodeReferences = null, int? maxResultLength = null, int? maxCallsPerSession = null, int? maxToolIterations = null, int? maxParallelToolCalls = null, int? showThoughtsAndTools = null, int? requestTimeout = null, bool? enableSubAgents = null, bool? showParallelBadge = null, bool? showContextWindowUsage = null, bool? showChatCost = null)
    {
        var settings = await _dbContext.UserAiSettings.FindAsync(userId);
        if (settings == null)
        {
            settings = new UserAiSettings { AspNetUserId = userId };
            _dbContext.UserAiSettings.Add(settings);
        }

        if (spoilerFreeMode.HasValue) settings.SpoilerFreeMode = spoilerFreeMode.Value;
        
        settings.MaxResultLength = maxResultLength;
        settings.MaxCallsPerSession = maxCallsPerSession;
        settings.MaxToolIterations = maxToolIterations;
        settings.MaxParallelToolCalls = maxParallelToolCalls;

        if (enableWebSearch.HasValue) settings.EnableWebSearch = enableWebSearch.Value;
        if (enableToolUse.HasValue) settings.EnableToolUse = enableToolUse.Value;
        if (enableSubAgents.HasValue) settings.EnableSubAgents = enableSubAgents.Value;
        if (enableClientTools.HasValue) settings.EnableClientTools = enableClientTools.Value;
        if (enableGameActions.HasValue) settings.EnableGameActions = enableGameActions.Value;
        if (showSourceCodeReferences.HasValue) settings.ShowSourceCodeReferences = showSourceCodeReferences.Value;
        if (showThoughtsAndTools.HasValue) settings.ShowThoughtsAndTools = showThoughtsAndTools.Value;
        if (requestTimeout.HasValue) settings.RequestTimeout = requestTimeout.Value;
        if (showParallelBadge.HasValue) settings.ShowParallelBadge = showParallelBadge.Value;
        if (showContextWindowUsage.HasValue) settings.ShowContextWindowUsage = showContextWindowUsage.Value;
        if (showChatCost.HasValue) settings.ShowChatCost = showChatCost.Value;

        await _dbContext.SaveChangesAsync();
    }

    public async Task SaveTitleGenerationModelAsync(string userId, long? modelId, bool isSystem = false, bool? disabled = null)
    {
        var settings = await _dbContext.UserAiSettings.FindAsync(userId);
        if (settings == null)
        {
            settings = new UserAiSettings { AspNetUserId = userId };
            _dbContext.UserAiSettings.Add(settings);
        }

        if (disabled.HasValue)
        {
            settings.TitleGenerationDisabled = disabled.Value;
        }
        else
        {
            settings.TitleGenerationDisabled = false;

            if (modelId.HasValue)
            {
                if (isSystem)
                {
                    // Note: Security check happens at runtime when using the model. We can just set it here.
                    settings.TitleGenerationSystemModelId = modelId;
                    settings.TitleGenerationModelId = null;
                }
                else
                {
                    // Verify model belongs to user
                    var modelExists = await _dbContext.UserAiModels.AnyAsync(m => m.Id == modelId.Value && m.AspNetUserId == userId);
                    if (!modelExists)
                    {
                        throw new ArgumentException("Model does not exist or does not belong to user.");
                    }
                    settings.TitleGenerationModelId = modelId;
                    settings.TitleGenerationSystemModelId = null;
                }
            }
            else
            {
                settings.TitleGenerationModelId = null;
                settings.TitleGenerationSystemModelId = null;
            }
        }

        await _dbContext.SaveChangesAsync();
    }


    public async Task<List<dynamic>> GetApiKeysStatusAsync(string userId)
    {
        var statuses = new List<dynamic>();
        
        foreach (var p in SupportedProviders)
        {
            var key = await _dbContext.UserAiApiKeys.FirstOrDefaultAsync(k => k.AspNetUserId == userId && k.Provider == p);
            statuses.Add(new { 
                Provider = p, 
                HasKey = key != null && !string.IsNullOrEmpty(key.EncryptedApiKey),
                ParallelExecutionMode = (int)(key?.ParallelExecutionMode ?? ParallelExecutionMode.Enabled)
            });
        }
        
        return statuses;
    }

    public async Task SaveApiKeyForProviderAsync(string userId, string provider, string apiKey)
    {
        var entry = await _dbContext.UserAiApiKeys.FirstOrDefaultAsync(k => k.AspNetUserId == userId && k.Provider == provider);
        if (entry == null)
        {
            entry = new UserAiApiKey { AspNetUserId = userId, Provider = provider };
            _dbContext.UserAiApiKeys.Add(entry);
        }

        var (ciphertext, nonce, tag) = _cryptoService.Encrypt(apiKey, userId);
        entry.EncryptedApiKey = ciphertext;
        entry.ApiKeyNonce = nonce;
        entry.ApiKeyTag = tag;

        await _dbContext.SaveChangesAsync();
    }

    public async Task SaveApiKeyParallelModeAsync(string userId, string provider, int mode)
    {
        var entry = await _dbContext.UserAiApiKeys.FirstOrDefaultAsync(k => k.AspNetUserId == userId && k.Provider == provider);
        if (entry == null)
        {
            entry = new UserAiApiKey { AspNetUserId = userId, Provider = provider };
            _dbContext.UserAiApiKeys.Add(entry);
        }

        entry.ParallelExecutionMode = (ParallelExecutionMode)mode;
        await _dbContext.SaveChangesAsync();
    }

    public async Task DeleteApiKeyForProviderAsync(string userId, string provider)
    {
        var entry = await _dbContext.UserAiApiKeys.FirstOrDefaultAsync(k => k.AspNetUserId == userId && k.Provider == provider);
        if (entry != null)
        {
            entry.EncryptedApiKey = null;
            entry.ApiKeyNonce = null;
            entry.ApiKeyTag = null;
            await _dbContext.SaveChangesAsync();
        }
    }

    public async Task<string?> GetDecryptedApiKeyForProviderAsync(string userId, string provider)
    {
        var key = await _dbContext.UserAiApiKeys.FirstOrDefaultAsync(k => k.AspNetUserId == userId && k.Provider == provider);
        if (key != null && !string.IsNullOrEmpty(key.EncryptedApiKey) && !string.IsNullOrEmpty(key.ApiKeyNonce) && !string.IsNullOrEmpty(key.ApiKeyTag))
        {
            return _cryptoService.Decrypt(key.EncryptedApiKey, key.ApiKeyNonce, key.ApiKeyTag, userId);
        }

        return null;
    }

    public async Task<List<UserAiModel>> GetUserModelsAsync(string userId)
    {
        return await _dbContext.UserAiModels
            .Where(m => m.AspNetUserId == userId)
            .OrderBy(m => m.OrderIndex)
            .ToListAsync();
    }

    public async Task<List<(SystemAiApiConfiguration Config, int ResolvedRole)>> GetResolvedSystemModelsAsync(string userId, int? roleFilter = null)
    {
        var userGroupIds = await _dbContext.UserGroups
            .Where(ug => ug.AspNetUserId == userId)
            .Select(ug => ug.GroupId)
            .ToListAsync();

        var query = from c in _dbContext.SystemAiApiConfigurations
                    where c.IsEnabled
                    let userAssignment = _dbContext.UserSystemAiApiConfigurations.FirstOrDefault(u => u.SystemAiApiConfigurationId == c.Id && u.AspNetUserId == userId && u.IsEnabled)
                    let groupAssignment = _dbContext.GroupSystemAiApiConfigurations.Where(g => g.SystemAiApiConfigurationId == c.Id && userGroupIds.Contains(g.GroupId) && g.IsEnabled).OrderBy(g => g.OrderIndex).FirstOrDefault()
                    where c.IsSystemWide || userAssignment != null || groupAssignment != null
                    select new {
                        Config = c,
                        ResolvedRole = userAssignment != null ? userAssignment.ModelRole :
                                       (groupAssignment != null ? groupAssignment.ModelRole : c.ModelRole),
                        UserOrder = userAssignment != null ? userAssignment.OrderIndex : (int?)null
                    };

        var rawList = await query.ToListAsync();
        
        var resultList = rawList
            .Where(x => roleFilter == null || (x.ResolvedRole & roleFilter.Value) == roleFilter.Value)
            .OrderBy(x => x.UserOrder.HasValue ? 0 : 1)
            .ThenBy(x => x.UserOrder ?? x.Config.OrderIndex)
            .Select(x => (x.Config, x.ResolvedRole))
            .ToList();

        return resultList;
    }

    public async Task AddUserModelAsync(string userId, UserAiModel model)
    {
        var count = await _dbContext.UserAiModels.CountAsync(m => m.AspNetUserId == userId);
        model.AspNetUserId = userId;
        model.OrderIndex = count;
        _dbContext.UserAiModels.Add(model);
        await _dbContext.SaveChangesAsync();
    }

    public async Task UpdateUserModelAsync(string userId, long id, string? displayName, string? displayNameMode, string? thinkingLevel, string? reasoningMode, string? reasoningSummary, string? serviceTier, int? maxInputTokens, int? maxOutputTokens, string? modelId = null, string? provider = null, string? pricingMode = null, decimal? inputPricePerMillion = null, decimal? outputPricePerMillion = null, decimal? cachedInputPricePerMillion = null)
    {
        var model = await _dbContext.UserAiModels.FirstOrDefaultAsync(m => m.Id == id && m.AspNetUserId == userId);
        if (model != null)
        {
            if (!string.IsNullOrEmpty(modelId)) model.ModelId = modelId;
            if (!string.IsNullOrEmpty(provider)) model.Provider = provider;
            if (displayName != null) model.DisplayName = displayName;
            if (displayNameMode != null) model.DisplayNameMode = displayNameMode;
            // thinkingLevel can be explicitly null to clear it
            model.ThinkingLevel = thinkingLevel;
            model.ReasoningMode = reasoningMode;
            model.ReasoningSummary = reasoningSummary;
            model.ServiceTier = serviceTier;
            
            // these can also be cleared by setting them to null from UI (which we allow if empty)
            // if we want to distinguish between "not updated" and "cleared", we could use a different pattern, 
            // but in the UI we'll just send the current value or null to clear.
            model.MaxInputTokens = maxInputTokens;
            model.MaxOutputTokens = maxOutputTokens;
            if (pricingMode != null) model.PricingMode = Overseer.Models.PricingModes.Normalize(pricingMode);
            model.InputPricePerMillion = inputPricePerMillion;
            model.OutputPricePerMillion = outputPricePerMillion;
            model.CachedInputPricePerMillion = cachedInputPricePerMillion;
            
            await _dbContext.SaveChangesAsync();
        }
    }

    public async Task DeleteUserModelAsync(string userId, long modelId)
    {
        var model = await _dbContext.UserAiModels.FirstOrDefaultAsync(m => m.Id == modelId && m.AspNetUserId == userId);
        if (model != null)
        {
            _dbContext.UserAiModels.Remove(model);
            await _dbContext.SaveChangesAsync();

            // Re-index remaining models
            var remaining = await _dbContext.UserAiModels
                .Where(m => m.AspNetUserId == userId)
                .OrderBy(m => m.OrderIndex)
                .ToListAsync();
                
            for (int i = 0; i < remaining.Count; i++)
            {
                remaining[i].OrderIndex = i;
            }
            await _dbContext.SaveChangesAsync();

            // Fallback for TitleGenerationModelId if the deleted model was in use
            var settings = await _dbContext.UserAiSettings.FindAsync(userId);
            if (settings != null && settings.TitleGenerationModelId == modelId)
            {
                if (remaining.Count > 0)
                {
                    settings.TitleGenerationModelId = remaining[0].Id;
                }
                else
                {
                    settings.TitleGenerationModelId = null;
                }
                await _dbContext.SaveChangesAsync();
            }
        }
    }

    public async Task ReorderUserModelsAsync(string userId, long[] orderedIds)
    {
        var models = await _dbContext.UserAiModels
            .Where(m => m.AspNetUserId == userId)
            .ToListAsync();

        for (int i = 0; i < orderedIds.Length; i++)
        {
            var model = models.FirstOrDefault(m => m.Id == orderedIds[i]);
            if (model != null)
            {
                model.OrderIndex = i;
            }
        }
        await _dbContext.SaveChangesAsync();
    }

    public async Task ReorderUserSystemModelsAsync(string userId, long[] orderedConfigIds)
    {
        var existingConfigs = await _dbContext.SystemAiApiConfigurations
            .Where(c => orderedConfigIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id);

        var existingAssignments = await _dbContext.UserSystemAiApiConfigurations
            .Where(u => u.AspNetUserId == userId && orderedConfigIds.Contains(u.SystemAiApiConfigurationId))
            .ToDictionaryAsync(u => u.SystemAiApiConfigurationId);

        for (int i = 0; i < orderedConfigIds.Length; i++)
        {
            var configId = orderedConfigIds[i];
            if (!existingConfigs.TryGetValue(configId, out var config)) continue;

            if (existingAssignments.TryGetValue(configId, out var assignment))
            {
                assignment.OrderIndex = i;
            }
            else
            {
                _dbContext.UserSystemAiApiConfigurations.Add(new UserSystemAiApiConfiguration
                {
                    AspNetUserId = userId,
                    SystemAiApiConfigurationId = configId,
                    OrderIndex = i,
                    IsEnabled = config.IsEnabled,
                    ModelRole = config.ModelRole
                });
            }
        }
        await _dbContext.SaveChangesAsync();
    }

    public async Task ResetSystemModelsOrderAsync(string userId)
    {
        await _dbContext.UserSystemAiApiConfigurations
            .Where(u => u.AspNetUserId == userId && u.OrderIndex != null)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.OrderIndex, (int?)null));
    }

    public async Task<string?> GetDecryptedSystemApiKeyAsync(long configId)
    {
        var config = await _dbContext.SystemAiApiConfigurations.FindAsync(configId);
        if (config != null && !string.IsNullOrEmpty(config.EncryptedApiKey) && !string.IsNullOrEmpty(config.ApiKeyNonce) && !string.IsNullOrEmpty(config.ApiKeyTag))
        {
            return _cryptoService.Decrypt(config.EncryptedApiKey, config.ApiKeyNonce, config.ApiKeyTag, "SYSTEM_API_KEY");
        }
        return null;
    }
}
