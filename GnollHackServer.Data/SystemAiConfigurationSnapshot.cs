namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

/// <summary>
/// One distinct combination of model settings a benchmark ever ran with. Append-only and
/// content-addressed: <see cref="Sha256"/> is the hash of the canonical form of every other
/// hashed field, so identical settings share one row, and no code updates or deletes a row.
/// History references these rows by foreign key; the mutable <see cref="SystemAiApiConfiguration"/>
/// is referenced only by a plain attribution id, so editing or deleting a configuration never
/// changes what history says it ran with.
///
/// Credentials, pricing, posture, rate limits and the chat-only input budget are deliberately not
/// stored: a snapshot identifies an instrument, never a key.
/// </summary>
public class SystemAiConfigurationSnapshot
{
    public long Id { get; set; }

    /// <summary>Lower-case hex SHA-256 of the canonical form. Unique.</summary>
    [MaxLength(64)]
    [Column(TypeName = "char(64)")]
    public string Sha256 { get; set; } = default!;

    /// <summary>
    /// False only for rows backfilled from legacy columns that did not record every setting. On
    /// such a row a null field means <b>not recorded</b>, never "not set".
    /// </summary>
    public bool IsComplete { get; set; } = true;

    [MaxLength(64)]
    public string Provider { get; set; } = default!;

    [MaxLength(128)]
    public string ModelId { get; set; } = default!;

    [MaxLength(256)]
    public string? DisplayName { get; set; }

    [MaxLength(32)]
    public string? ThinkingLevel { get; set; }

    [MaxLength(32)]
    public string? ReasoningMode { get; set; }

    [MaxLength(32)]
    public string? ReasoningSummary { get; set; }

    [MaxLength(64)]
    public string? ServiceTier { get; set; }

    /// <summary>The configuration's own output cap, before any harness fallback.</summary>
    public int? MaxOutputTokens { get; set; }

    /// <summary>Null only when <see cref="IsComplete"/> is false.</summary>
    public ParallelExecutionMode? ParallelExecutionMode { get; set; }

    /// <summary>Null means the provider's official endpoint.</summary>
    [MaxLength(2048)]
    public string? BaseUrl { get; set; }

    [MaxLength(64)]
    public string? ApiVersion { get; set; }

    [MaxLength(4096)]
    public string? CustomHeadersJson { get; set; }

    /// <summary>When the row was first captured. Not hashed.</summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
