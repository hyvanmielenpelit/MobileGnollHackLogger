namespace MobileGnollHackLogger.Data;

using System.ComponentModel.DataAnnotations;

public class UserAiApiKey
{
    public long Id { get; set; }

    [MaxLength(450)]
    public string AspNetUserId { get; set; } = default!;
    
    public ApplicationUser? AspNetUser { get; set; }

    [MaxLength(64)]
    public string Provider { get; set; } = default!;  // "OpenAI", "Anthropic", "Google"

    [MaxLength(2048)]
    public string? EncryptedApiKey { get; set; }
    
    [MaxLength(32)]
    public string? ApiKeyNonce { get; set; }
    
    [MaxLength(32)]
    public string? ApiKeyTag { get; set; }

    public ParallelExecutionMode ParallelExecutionMode { get; set; } = ParallelExecutionMode.Enabled;

    /// <summary>
    /// The posture the user declares for their own provider account, as a
    /// ProviderConfidentialityPosture name. Null = never declared, treated as "Unknown".
    /// </summary>
    /// <remarks>
    /// **Self-declared and unverifiable by Overseer.** Nothing here is checked against anything;
    /// the user is describing an agreement only they can see. The resolver therefore never
    /// treats this as equivalent to an operator-verified posture on a system configuration, and
    /// a value here can never produce a green privacy badge — otherwise anyone could tick
    /// "ZeroRetention" and unlock a claim the product cannot make.
    /// </remarks>
    [MaxLength(32)]
    public string? ConfidentialityPosture { get; set; }

    /// <summary>The user's own note about their provider account's terms.</summary>
    [MaxLength(1024)]
    public string? ConfidentialityNote { get; set; }

    /// <summary>When the user last declared a posture for this key.</summary>
    public DateTime? PostureDeclaredUtc { get; set; }

    /// <summary>
    /// The user's judgement on whether this key is adequate for confidential sessions.
    /// Null = undecided, which is what the AskWhenUnclear gate asks about — once per key, not
    /// once per turn. False is a decision, not an absence: it refuses the key.
    /// </summary>
    public bool? UserTrustsForConfidential { get; set; }

    /// <summary>When <see cref="UserTrustsForConfidential"/> was last set.</summary>
    public DateTime? ConfidentialTrustDecidedUtc { get; set; }

    /* The custom-endpoint columns below carry the shape but are NOT writable by users while
       PrivacySettings:CustomEndpoints:AllowUserSuppliedBaseUrl is false, which is its value for
       this framework version. The API refuses a user-supplied value rather than storing and
       ignoring it, and EndpointPolicy.Resolve(UserAiApiKey) returns the official endpoint
       regardless of what the row holds.

       They exist now so that enabling user-supplied endpoints later is a policy change and a
       form, not a migration. A user may say what their provider's terms are; they may not
       decide where the server sends its outbound traffic. */

    /// <summary>Scheme and authority of a custom endpoint. See the note above: not user-writable.</summary>
    [MaxLength(2048)]
    public string? BaseUrl { get; set; }

    /// <summary>Extra request headers as a flat JSON object. See the note above: not user-writable.</summary>
    [MaxLength(4096)]
    public string? CustomHeadersJson { get; set; }

    /// <summary>The Azure OpenAI <c>api-version</c>. See the note above: not user-writable.</summary>
    [MaxLength(64)]
    public string? ApiVersion { get; set; }
}

