using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;

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

    public async Task SaveSettingsAsync(string userId, string? defaultProvider, string? defaultModel, string? apiKey, string? thinkingLevel = null, bool? spoilerFreeMode = null, int? maxInputTokens = null, int? maxOutputTokens = null, bool? enableWebSearch = null, bool? enableToolUse = null, bool? enableClientTools = null, bool? enableGameActions = null, bool? allowMultipleModels = null)
    {
        var settings = await _dbContext.UserAiSettings.FindAsync(userId);
        if (settings == null)
        {
            settings = new UserAiSettings { AspNetUserId = userId };
            _dbContext.UserAiSettings.Add(settings);
        }

        if (defaultProvider != null) settings.DefaultProvider = defaultProvider;
        if (defaultModel != null) settings.DefaultModel = defaultModel;
        if (thinkingLevel != null) settings.ThinkingLevel = thinkingLevel;
        if (spoilerFreeMode.HasValue) settings.SpoilerFreeMode = spoilerFreeMode.Value;
        
        // Always update token limits if provided; allow nulling them out if -1 or something? 
        // We'll just overwrite. We should probably clear them if null is sent, but the UI might just not send them.
        // Wait, the UI sends null when the user clears the input box. So we should set the DB value to the passed value.
        settings.MaxInputTokens = maxInputTokens;
        settings.MaxOutputTokens = maxOutputTokens;

        if (enableWebSearch.HasValue) settings.EnableWebSearch = enableWebSearch.Value;
        if (enableToolUse.HasValue) settings.EnableToolUse = enableToolUse.Value;
        if (enableClientTools.HasValue) settings.EnableClientTools = enableClientTools.Value;
        if (enableGameActions.HasValue) settings.EnableGameActions = enableGameActions.Value;
        if (allowMultipleModels.HasValue) settings.AllowMultipleModels = allowMultipleModels.Value;

        if (!string.IsNullOrEmpty(apiKey))
        {
            var (ciphertext, nonce, tag) = _cryptoService.Encrypt(apiKey, userId);
            settings.EncryptedApiKey = ciphertext;
            settings.ApiKeyNonce = nonce;
            settings.ApiKeyTag = tag;
        }

        await _dbContext.SaveChangesAsync();
    }

    public async Task<string?> GetDecryptedApiKeyAsync(string userId)
    {
        var settings = await _dbContext.UserAiSettings.FindAsync(userId);
        if (settings == null || string.IsNullOrEmpty(settings.EncryptedApiKey) || string.IsNullOrEmpty(settings.ApiKeyNonce) || string.IsNullOrEmpty(settings.ApiKeyTag))
        {
            return null;
        }

        return _cryptoService.Decrypt(settings.EncryptedApiKey, settings.ApiKeyNonce, settings.ApiKeyTag, userId);
    }

    public async Task DeleteApiKeyAsync(string userId)
    {
        var settings = await _dbContext.UserAiSettings.FindAsync(userId);
        if (settings != null)
        {
            settings.EncryptedApiKey = null;
            settings.ApiKeyNonce = null;
            settings.ApiKeyTag = null;
            await _dbContext.SaveChangesAsync();
        }
    }

    public async Task<List<dynamic>> GetApiKeysStatusAsync(string userId)
    {
        var statuses = new List<dynamic>();
        var providers = new[] { "OpenAI", "Anthropic", "Google" };
        
        foreach (var p in providers)
        {
            var key = await _dbContext.UserAiApiKeys.FirstOrDefaultAsync(k => k.AspNetUserId == userId && k.Provider == p);
            statuses.Add(new { Provider = p, HasKey = key != null && !string.IsNullOrEmpty(key.EncryptedApiKey) });
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

    public async Task DeleteApiKeyForProviderAsync(string userId, string provider)
    {
        var entry = await _dbContext.UserAiApiKeys.FirstOrDefaultAsync(k => k.AspNetUserId == userId && k.Provider == provider);
        if (entry != null)
        {
            _dbContext.UserAiApiKeys.Remove(entry);
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

        // Fallback to legacy
        var settings = await _dbContext.UserAiSettings.FindAsync(userId);
        if (settings != null && settings.DefaultProvider == provider && !string.IsNullOrEmpty(settings.EncryptedApiKey) && !string.IsNullOrEmpty(settings.ApiKeyNonce) && !string.IsNullOrEmpty(settings.ApiKeyTag))
        {
            return _cryptoService.Decrypt(settings.EncryptedApiKey, settings.ApiKeyNonce, settings.ApiKeyTag, userId);
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

    public async Task AddUserModelAsync(string userId, UserAiModel model)
    {
        var count = await _dbContext.UserAiModels.CountAsync(m => m.AspNetUserId == userId);
        model.AspNetUserId = userId;
        model.OrderIndex = count;
        _dbContext.UserAiModels.Add(model);
        await _dbContext.SaveChangesAsync();
    }

    public async Task UpdateUserModelAsync(string userId, long modelId, string? displayName, string? thinkingLevel, int? maxInputTokens, int? maxOutputTokens)
    {
        var model = await _dbContext.UserAiModels.FirstOrDefaultAsync(m => m.Id == modelId && m.AspNetUserId == userId);
        if (model != null)
        {
            if (displayName != null) model.DisplayName = displayName;
            // thinkingLevel can be explicitly null to clear it
            model.ThinkingLevel = thinkingLevel;
            
            // these can also be cleared by setting them to null from UI (which we allow if empty)
            // if we want to distinguish between "not updated" and "cleared", we could use a different pattern, 
            // but in the UI we'll just send the current value or null to clear.
            model.MaxInputTokens = maxInputTokens;
            model.MaxOutputTokens = maxOutputTokens;
            
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
}
