namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>
/// The default API key of one provider. System AI configurations with
/// <see cref="SystemAiApiConfiguration.UseDefaultApiKey"/> hold a copy of it in their own key
/// columns; this table has no relationship to them.
/// </summary>
public class SystemDefaultApiKey
{
    public int Id { get; set; }

    [Required]
    [MaxLength(64)]
    public string Provider { get; set; } = default!;

    [MaxLength(2048)]
    public string? EncryptedApiKey { get; set; }

    [MaxLength(32)]
    public string? ApiKeyNonce { get; set; }

    [MaxLength(32)]
    public string? ApiKeyTag { get; set; }

    /// <summary>The last four characters of the key, for recognizing it. Never more.</summary>
    [MaxLength(8)]
    public string? KeyHint { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public ApiKeyVerificationStatus? ApiKeyVerification { get; set; }

    /// <summary>When the key was last checked with its provider, whatever the outcome.</summary>
    public DateTime? ApiKeyVerificationCheckedAtUtc { get; set; }

    /// <summary>
    /// The failure detail of a <see cref="ApiKeyVerificationStatus.NotVerified"/> key, or a provider
    /// warning for a verified one. Never holds the key.
    /// </summary>
    [MaxLength(1000)]
    public string? ApiKeyVerificationMessage { get; set; }
}
