namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>
/// Throttle and occurrence state of the failure alert email for one operator API key. One row per
/// key value, not per configuration: a default key is copied into every configuration that uses it.
/// Never holds the key.
/// </summary>
public class ApiKeyFailureAlertState
{
    public long Id { get; set; }

    /// <summary>Lower-case hex SHA-256 of a domain prefix plus the key. Unique.</summary>
    [Required]
    [MaxLength(64)]
    public string KeyFingerprint { get; set; } = default!;

    [MaxLength(64)]
    public string? Provider { get; set; }

    /// <summary>The first failure since the last email.</summary>
    public DateTime FirstOccurredUtc { get; set; }

    public DateTime LastOccurredUtc { get; set; }

    /// <summary>Null until an email has gone out.</summary>
    public DateTime? LastEmailSentUtc { get; set; }

    /// <summary>Includes the occurrence that triggered the email.</summary>
    public int OccurrencesSinceLastEmail { get; set; }

    /// <summary><c>InsufficientBalance</c> or <c>KeyRejected</c>.</summary>
    [MaxLength(32)]
    public string? LastFailureKind { get; set; }

    /// <summary>No foreign key: the configuration may be deleted, and this history outlives it.</summary>
    public long? LastSystemAiApiConfigurationId { get; set; }
}
