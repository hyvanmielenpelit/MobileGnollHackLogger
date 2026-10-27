namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>
/// One game text the snapshot guard found something in, kept for an administrator's review.
/// Encrypted at rest under the content keyring; deleted with its SecurityEvent and by its own, shorter
/// retention (ChatRetentionSettings.PruneSnapshotQuarantineDays). Written in every chat mode, which the
/// privacy notice and the incognito and confidential descriptions state.
/// </summary>
public class SnapshotGuardQuarantine
{
    public long Id { get; set; }

    public long SecurityEventId { get; set; }
    public SecurityEvent SecurityEvent { get; set; } = default!;

    public DateTime OccurredUtc { get; set; }

    /// <summary>A plain column, not a key: a second cascade path from AspNetUsers is refused by SQL Server.</summary>
    [MaxLength(450)]
    public string? AspNetUserId { get; set; }

    /// <summary>Where the text arrived: create, history, manifest, attach or tool-&lt;name&gt;.</summary>
    [MaxLength(64)]
    public string Channel { get; set; } = default!;

    /// <summary>A SnapshotGuardSubject name.</summary>
    [MaxLength(16)]
    public string Subject { get; set; } = default!;

    /// <summary>A SnapshotGuardTextKind name.</summary>
    [MaxLength(16)]
    public string TextKind { get; set; } = default!;

    /// <summary>Suspicious or Injection.</summary>
    [MaxLength(16)]
    public string Level { get; set; } = default!;

    /// <summary>The verdict's codes, comma-joined.</summary>
    [MaxLength(256)]
    public string Codes { get; set; } = default!;

    public bool PlayerAuthored { get; set; }

    /// <summary>False in Observe mode.</summary>
    public bool Withheld { get; set; }

    /// <summary>normal, confidential or incognito.</summary>
    [MaxLength(16)]
    public string SessionMode { get; set; } = default!;

    /// <summary>The wire form (a decimal id or eph_&lt;guid&gt;), when the caller knew it; not a key.</summary>
    [MaxLength(64)]
    public string? SessionRef { get; set; }

    /// <summary>Characters of the full text.</summary>
    public int PlainLength { get; set; }

    /// <summary>Characters encrypted, at most SnapshotGuard:QuarantineMaxChars.</summary>
    public int StoredLength { get; set; }

    public bool Truncated { get; set; }

    /// <summary>Lower-case hex SHA-256 of the full text's UTF-8.</summary>
    [MaxLength(64)]
    public string Sha256 { get; set; } = default!;

    /// <summary>The content keyring version the text is encrypted under.</summary>
    [MaxLength(32)]
    public string KeyVersion { get; set; } = default!;

    /// <summary>Base64.</summary>
    public string Ciphertext { get; set; } = default!;

    [MaxLength(64)]
    public string Nonce { get; set; } = default!;

    [MaxLength(64)]
    public string Tag { get; set; } = default!;
}
