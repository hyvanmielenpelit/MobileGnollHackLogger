using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Overseer.Models;

namespace Overseer.Services;

public enum DefaultApiKeySaveKind { Saved, UnknownProvider, EmptyKey, Invalid, Unverifiable }

/// <summary>
/// The outcome of <see cref="SystemDefaultApiKeyService.SaveAsync"/>: the saved result, the refused
/// validation for <see cref="DefaultApiKeySaveKind.Invalid"/> and <see cref="DefaultApiKeySaveKind.Unverifiable"/>,
/// or an error message for a request that was never checked.
/// </summary>
public sealed record DefaultApiKeySaveOutcome(
    DefaultApiKeySaveKind Kind,
    DefaultApiKeySaveResultDto? Result = null,
    ApiKeyValidationResult? Validation = null,
    string? Error = null);

/// <summary>
/// The per-provider default API keys that system AI configurations can use. A configuration with
/// <see cref="SystemAiApiConfiguration.UseDefaultApiKey"/> holds a copy of its provider's default key
/// in its own key columns, encrypted as <c>"SYSTEM_API_KEY"</c>; every save and delete here rewrites
/// those copies in the same <c>SaveChangesAsync</c>.
/// </summary>
public class SystemDefaultApiKeyService
{
    public const string AssociatedDataPrefix = "SYSTEM_DEFAULT_API_KEY:";
    private const string ConfigAssociatedData = "SYSTEM_API_KEY";
    private const int MaxVerificationMessageLength = 1000;
    private const int MinKeyLengthForHint = 8;

    /// <summary>The supported providers in display order: Anthropic, Google, OpenAI.</summary>
    public static readonly IReadOnlyList<string> Providers =
        SettingsService.SupportedProviders.OrderBy(p => p, StringComparer.Ordinal).ToArray();

    private readonly ApplicationDbContext _db;
    private readonly CryptoService _crypto;
    private readonly IApiKeyValidator _validator;
    private readonly ILogger<SystemDefaultApiKeyService> _logger;

    public SystemDefaultApiKeyService(
        ApplicationDbContext db,
        CryptoService crypto,
        IApiKeyValidator validator,
        ILogger<SystemDefaultApiKeyService> logger)
    {
        _db = db;
        _crypto = crypto;
        _validator = validator;
        _logger = logger;
    }

    /// <summary>The associated data a provider's default key is encrypted with.</summary>
    public static string AssociatedDataFor(string provider) => AssociatedDataPrefix + provider;

    /// <summary>Case-insensitive match against the supported providers, returning the canonical casing.</summary>
    public static bool TryCanonicalizeProvider(string? provider, out string canonical)
    {
        canonical = Providers.FirstOrDefault(p => p.Equals(provider?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        return canonical.Length > 0;
    }

    public static string UnsupportedProviderMessage(string? provider) =>
        $"Unsupported provider '{provider}'. Supported providers are: {string.Join(", ", Providers)}.";

    public async Task<List<DefaultApiKeyStatusDto>> GetStatusAsync(CancellationToken ct = default)
    {
        var rows = await _db.SystemDefaultApiKeys.AsNoTracking().ToListAsync(ct);
        var users = await LoadUsersAsync(ct);

        return Providers
            .Select(p => BuildStatus(
                p,
                rows.FirstOrDefault(r => string.Equals(r.Provider, p, StringComparison.OrdinalIgnoreCase)),
                users.Where(u => string.Equals(u.Provider, p, StringComparison.OrdinalIgnoreCase)).Select(u => u.Dto).ToList()))
            .ToList();
    }

    public async Task<DefaultApiKeySaveOutcome> SaveAsync(string provider, string? apiKey, bool saveUnverified, CancellationToken ct = default)
    {
        if (!TryCanonicalizeProvider(provider, out var canonical))
        {
            return new DefaultApiKeySaveOutcome(DefaultApiKeySaveKind.UnknownProvider, Error: UnsupportedProviderMessage(provider));
        }

        var key = apiKey?.Trim() ?? string.Empty;
        if (key.Length == 0)
        {
            return new DefaultApiKeySaveOutcome(DefaultApiKeySaveKind.EmptyKey, Error: "The API key cannot be empty.");
        }

        var validation = await _validator.ValidateAsync(canonical, key, ct);
        _logger.LogInformation("Default {Provider} API key checked on save: {Verdict}.", canonical, validation.Verdict);

        if (validation.Verdict == ApiKeyVerdict.Invalid)
        {
            return new DefaultApiKeySaveOutcome(DefaultApiKeySaveKind.Invalid, Validation: validation);
        }
        if (validation.Verdict == ApiKeyVerdict.Unverifiable && !saveUnverified)
        {
            return new DefaultApiKeySaveOutcome(DefaultApiKeySaveKind.Unverifiable, Validation: validation);
        }

        var now = DateTime.UtcNow;
        var row = await _db.SystemDefaultApiKeys.FirstOrDefaultAsync(k => k.Provider == canonical, ct);
        if (row == null)
        {
            row = new SystemDefaultApiKey { Provider = canonical, CreatedAtUtc = now };
            _db.SystemDefaultApiKeys.Add(row);
        }

        var (ciphertext, nonce, tag) = _crypto.Encrypt(key, AssociatedDataFor(canonical));
        row.EncryptedApiKey = ciphertext;
        row.ApiKeyNonce = nonce;
        row.ApiKeyTag = tag;
        row.KeyHint = key.Length >= MinKeyLengthForHint ? key[^4..] : null;
        row.UpdatedAtUtc = now;

        string? warning = null;
        switch (validation.Verdict)
        {
            case ApiKeyVerdict.ValidWithWarning:
                warning = validation.Message;
                SetVerification(row, ApiKeyVerificationStatus.Verified, warning, now);
                break;
            case ApiKeyVerdict.Unverifiable:
                SetVerification(row, ApiKeyVerificationStatus.NotVerified, validation.Detail?.ToText() ?? validation.Message, now);
                break;
            default:
                SetVerification(row, ApiKeyVerificationStatus.Verified, null, now);
                break;
        }

        var configs = await LoadDefaultConfigsAsync(canonical, ct);
        foreach (var config in configs)
        {
            WriteConfigCopy(_crypto, config, key);
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Default {Provider} API key saved; {Count} configuration copies rewritten.", canonical, configs.Count);

        return new DefaultApiKeySaveOutcome(DefaultApiKeySaveKind.Saved, Result: new DefaultApiKeySaveResultDto
        {
            Status = await GetProviderStatusAsync(canonical, ct),
            UpdatedConfigCount = configs.Count,
            Warning = warning
        });
    }

    /// <summary>
    /// Re-checks the stored key and updates only its verification columns. Null when the provider is
    /// unknown or has no default key.
    /// </summary>
    public async Task<DefaultApiKeyStatusDto?> VerifyAgainAsync(string provider, CancellationToken ct = default)
    {
        if (!TryCanonicalizeProvider(provider, out var canonical)) return null;

        var row = await _db.SystemDefaultApiKeys.FirstOrDefaultAsync(k => k.Provider == canonical, ct);
        if (row == null || string.IsNullOrEmpty(row.EncryptedApiKey)) return null;

        var key = _crypto.Decrypt(row.EncryptedApiKey, row.ApiKeyNonce ?? string.Empty, row.ApiKeyTag ?? string.Empty, AssociatedDataFor(canonical));
        var validation = await _validator.ValidateAsync(canonical, key, ct);
        _logger.LogInformation("Default {Provider} API key verified again: {Verdict}.", canonical, validation.Verdict);

        var now = DateTime.UtcNow;
        var detailText = validation.Detail?.ToText() ?? validation.Message;
        switch (validation.Verdict)
        {
            case ApiKeyVerdict.Valid:
                SetVerification(row, ApiKeyVerificationStatus.Verified, null, now);
                break;
            case ApiKeyVerdict.ValidWithWarning:
                SetVerification(row, ApiKeyVerificationStatus.Verified, validation.Message, now);
                break;
            case ApiKeyVerdict.Invalid:
                SetVerification(row, ApiKeyVerificationStatus.NotVerified, $"{canonical} now rejects this key. {detailText}", now);
                break;
            default:
                SetVerification(row, ApiKeyVerificationStatus.NotVerified, detailText, now);
                break;
        }

        await _db.SaveChangesAsync(ct);
        return await GetProviderStatusAsync(canonical, ct);
    }

    /// <summary>The configurations a delete would disable. Null when the provider is unknown.</summary>
    public async Task<DefaultApiKeyDeletionCheckDto?> GetDeletionCheckAsync(string provider, CancellationToken ct = default)
    {
        if (!TryCanonicalizeProvider(provider, out var canonical)) return null;

        var users = (await LoadUsersAsync(ct))
            .Where(u => string.Equals(u.Provider, canonical, StringComparison.OrdinalIgnoreCase))
            .Select(u => u.Dto)
            .ToList();
        return new DefaultApiKeyDeletionCheckDto { Count = users.Count, Configs = users };
    }

    /// <summary>
    /// Removes the default key, clears every configuration copy of it and disables those
    /// configurations, which keep <see cref="SystemAiApiConfiguration.UseDefaultApiKey"/>. Returns the
    /// number disabled, or null when the provider is unknown or has no default key.
    /// </summary>
    public async Task<int?> DeleteAsync(string provider, CancellationToken ct = default)
    {
        if (!TryCanonicalizeProvider(provider, out var canonical)) return null;

        var row = await _db.SystemDefaultApiKeys.FirstOrDefaultAsync(k => k.Provider == canonical, ct);
        if (row == null) return null;

        _db.SystemDefaultApiKeys.Remove(row);

        var configs = await LoadDefaultConfigsAsync(canonical, ct);
        foreach (var config in configs)
        {
            config.EncryptedApiKey = null;
            config.ApiKeyNonce = null;
            config.ApiKeyTag = null;
            config.IsEnabled = false;
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Default {Provider} API key deleted; {Count} configurations disabled.", canonical, configs.Count);
        return configs.Count;
    }

    /// <summary>
    /// Writes a copy of the default key of <paramref name="config"/>'s provider into its key columns.
    /// False when that provider has no default key. Does not save.
    /// </summary>
    public static async Task<bool> ApplyDefaultKeyAsync(ApplicationDbContext db, CryptoService crypto, SystemAiApiConfiguration config, CancellationToken ct)
    {
        if (!TryCanonicalizeProvider(config.Provider, out var canonical)) return false;

        var row = await db.SystemDefaultApiKeys.AsNoTracking().FirstOrDefaultAsync(k => k.Provider == canonical, ct);
        if (row == null || string.IsNullOrEmpty(row.EncryptedApiKey)) return false;

        var key = crypto.Decrypt(row.EncryptedApiKey, row.ApiKeyNonce ?? string.Empty, row.ApiKeyTag ?? string.Empty, AssociatedDataFor(canonical));
        WriteConfigCopy(crypto, config, key);
        return true;
    }

    private static void WriteConfigCopy(CryptoService crypto, SystemAiApiConfiguration config, string key)
    {
        var (ciphertext, nonce, tag) = crypto.Encrypt(key, ConfigAssociatedData);
        config.EncryptedApiKey = ciphertext;
        config.ApiKeyNonce = nonce;
        config.ApiKeyTag = tag;
    }

    private static void SetVerification(SystemDefaultApiKey row, ApiKeyVerificationStatus status, string? message, DateTime checkedAtUtc)
    {
        row.ApiKeyVerification = status;
        row.ApiKeyVerificationCheckedAtUtc = checkedAtUtc;
        row.ApiKeyVerificationMessage = message == null || message.Length <= MaxVerificationMessageLength
            ? message
            : message[..MaxVerificationMessageLength];
    }

    private async Task<List<SystemAiApiConfiguration>> LoadDefaultConfigsAsync(string canonical, CancellationToken ct)
    {
        var configs = await _db.SystemAiApiConfigurations.Where(c => c.UseDefaultApiKey).ToListAsync(ct);
        return configs.Where(c => string.Equals(c.Provider, canonical, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private async Task<List<(string Provider, DefaultApiKeyUserDto Dto)>> LoadUsersAsync(CancellationToken ct)
    {
        var configs = await _db.SystemAiApiConfigurations
            .AsNoTracking()
            .Where(c => c.UseDefaultApiKey)
            .OrderBy(c => c.OrderIndex)
            .ThenBy(c => c.Id)
            .Select(c => new { c.Provider, c.Id, c.DisplayName, c.IsEnabled })
            .ToListAsync(ct);

        return configs
            .Select(c => (c.Provider, new DefaultApiKeyUserDto { Id = c.Id, DisplayName = c.DisplayName, IsEnabled = c.IsEnabled }))
            .ToList();
    }

    private async Task<DefaultApiKeyStatusDto> GetProviderStatusAsync(string canonical, CancellationToken ct)
    {
        var row = await _db.SystemDefaultApiKeys.AsNoTracking().FirstOrDefaultAsync(k => k.Provider == canonical, ct);
        var users = (await LoadUsersAsync(ct))
            .Where(u => string.Equals(u.Provider, canonical, StringComparison.OrdinalIgnoreCase))
            .Select(u => u.Dto)
            .ToList();
        return BuildStatus(canonical, row, users);
    }

    private static DefaultApiKeyStatusDto BuildStatus(string provider, SystemDefaultApiKey? row, List<DefaultApiKeyUserDto> users)
    {
        var hasKey = row != null && !string.IsNullOrEmpty(row.EncryptedApiKey);
        return new DefaultApiKeyStatusDto
        {
            Provider = provider,
            HasKey = hasKey,
            KeyHint = hasKey ? row!.KeyHint : null,
            UpdatedAtUtc = hasKey ? row!.UpdatedAtUtc : null,
            Verification = ApiKeyVerificationDto.From(
                hasKey ? row!.ApiKeyVerification : null,
                hasKey ? row!.ApiKeyVerificationCheckedAtUtc : null,
                hasKey ? row!.ApiKeyVerificationMessage : null),
            UsedBy = users
        };
    }
}
