namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>The kind of a security event. Stored as a short; the values are part of the stored contract.</summary>
public enum SecurityEventKind : short
{
    LoginFailed = 1, LoginLockedOut = 2, TwoFactorFailed = 3,
    RateLimitRejected = 10, QuotaRefused = 11, AccessDenied = 12,
    ProviderRefusal = 20, ProviderSafetyBlock = 21, ProviderPolicyRejection = 22,
    AttachmentRejected = 30, ChatReported = 31, SnapshotRejected = 32, AutoBlocked = 33,
    AdminBlocked = 40, AdminUnblocked = 41,
    AccountHoldPlaced = 42, AccountHoldReleased = 43, AccountDeletionHeld = 44,
    AdminLockedOut = 45, AdminUnlocked = 46, AdminBanned = 47, AdminUnbanned = 48,
    AdminSignedOut = 49, AdminEmailConfirmed = 50, AdminTwoFactorReset = 51,
    AdminEmailChanged = 52,
    AdminGameSignInBlockCleared = 53,
}

/// <summary>
/// One security-relevant event, or a run of identical ones within ten minutes (<see cref="Count"/>). Holds
/// identifiers and codes only — never message text, attempted user names, passwords or headers.
/// Pruned by ChatRetentionSettings.PruneSecurityEventDays.
/// </summary>
public class SecurityEvent
{
    public long Id { get; set; }

    /// <summary>First occurrence of the run.</summary>
    public DateTime OccurredUtc { get; set; }

    public DateTime LastOccurredUtc { get; set; }

    public SecurityEventKind Kind { get; set; }

    public int Count { get; set; } = 1;

    [MaxLength(450)]
    public string? AspNetUserId { get; set; }
    public ApplicationUser? AspNetUser { get; set; }

    /// <summary>Authentication and rate-limit events only.</summary>
    [MaxLength(45)]
    public string? IpAddress { get; set; }

    /// <summary>Attribution only, not a foreign key.</summary>
    public long? SystemAiApiConfigurationId { get; set; }

    [MaxLength(32)]
    public string? Provider { get; set; }

    [MaxLength(128)]
    public string? ModelId { get; set; }

    /// <summary>A fixed-vocabulary or sanitized code: a policy name, a limit source, a finish reason, a provider error code.</summary>
    [MaxLength(64)]
    public string? Code { get; set; }

    /// <summary>Fixed-vocabulary detail ("unknown-user", an admin's block reason); never user content.</summary>
    [MaxLength(256)]
    public string? Detail { get; set; }
}
