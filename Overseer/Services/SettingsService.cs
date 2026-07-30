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

    public async Task SaveSettingsAsync(string userId, string? defaultProvider, string? defaultModel, string? apiKey, string? thinkingLevel = null, bool? spoilerFreeMode = null)
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
}
