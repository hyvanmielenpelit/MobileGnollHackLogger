namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

public class ChatSession
{
    public long Id { get; set; }
    
    public string? AspNetUserId { get; set; }
    public ApplicationUser? AspNetUser { get; set; }
    
    /// <summary>
    /// The session title, enveloped when the session is confidential.
    /// </summary>
    /// <remarks>
    /// **2048 characters is headroom for the envelope, not a business rule.** The plaintext cap
    /// is 256 and lives in the application — <c>ChatController.UpdateSessionTitle</c> and
    /// <c>ChatService.GenerateTitleAsync</c> — because widening this column removed the only
    /// thing that used to enforce it. An envelope costs 49 characters of prefix plus base64's
    /// 4/3 over up to 4 bytes per character, so 256 plaintext characters needs 1417; a
    /// 2048-character plaintext would need 10,973, and the overflow this width exists to
    /// prevent would simply return one ceiling higher.
    /// </remarks>
    [MaxLength(2048)]
    public string? Title { get; set; }
    
    public DateTime CreatedUtc { get; set; }
    
    public DateTime LastMessageUtc { get; set; }

    [MaxLength(4096)]
    public string? ClientSettings { get; set; }
    
    public bool IsGnollHackSession { get; set; }

    public bool IsDeleted { get; set; } = false;

    public DateTime? DeletedUtc { get; set; }

    public bool IsPinned { get; set; } = false;

    [MaxLength(32)]
    public string? DeletionReason { get; set; }

    /// <summary>
    /// Running total of <see cref="ChatMessage.EstimatedCost"/> across this session's
    /// assistant turns, in USD, accumulated at turn completion. Null until the first priced
    /// turn — absent pricing is never treated as zero. Denormalized on purpose: it must
    /// survive independently of which messages a client happens to load.
    /// </summary>
    public decimal? TotalEstimatedCost { get; set; }

    /// <summary>
    /// The part of <see cref="TotalEstimatedCost"/> that the user's own models produced, in USD. Turns
    /// funded by a system AI configuration are excluded: the operator pays for those, so they are not part
    /// of what this chat cost the user. Denormalized for the same reason as
    /// <see cref="TotalEstimatedCost"/> — it must survive independently of which messages a client loads.
    /// Null until the first priced user-model turn; absent pricing is never treated as zero.
    /// Invariant: never greater than <see cref="TotalEstimatedCost"/>.
    /// </summary>
    public decimal? TotalUserEstimatedCost { get; set; }

    /// <summary>
    /// Whether this session is in Confidentiality Mode.
    /// </summary>
    /// <remarks>
    /// **One-way.** A session is created normal or confidential and may be upgraded, never
    /// downgraded: history stored under a stronger promise must not become readable again by
    /// flipping a switch, and content already sent to a provider under that promise cannot be
    /// recalled. The upgrade endpoint returns 409 for confidential to normal.
    /// </remarks>
    public bool IsConfidential { get; set; } = false;

    /// <summary>When the session was upgraded. Null for one created confidential, and for a normal one.</summary>
    public DateTime? ConfidentialUpgradedUtc { get; set; }

    /// <summary>
    /// The resolved confidential policy as it stood at creation or upgrade, as JSON.
    /// </summary>
    /// <remarks>
    /// Snapshotted rather than resolved per turn so that a later change to the user's defaults,
    /// or to the administrator's floor, cannot retroactively weaken a promise already made
    /// about this session's content.
    /// </remarks>
    [MaxLength(2048)]
    public string? ConfidentialPolicyJson { get; set; }

    /// <summary>
    /// Days of inactivity before this session expires, or null to use the global TTL.
    /// </summary>
    /// <remarks>
    /// Materialised from the policy snapshot rather than read out of it, because
    /// <c>SoftDeleteInactiveSessionsAsync</c> is one set-based <c>ExecuteUpdateAsync</c> over
    /// every user's sessions against a single scalar. A per-user value cannot reach that query,
    /// and a JSON column cannot be filtered there portably — so retention runs one pass for the
    /// global TTL and one per distinct value here.
    /// </remarks>
    public int? EffectiveRetentionDays { get; set; }

    /// <summary>
    /// Whether deleting this session purges it immediately instead of moving it to the trash.
    /// </summary>
    /// <remarks>
    /// Materialised for the same reason as <see cref="EffectiveRetentionDays"/>, and it has to
    /// be honoured by **every** deletion path. The grace period is 30 days, so a confidential
    /// session with a 30-day TTL that soft-deletes on expiry would be retained for 60 — under a
    /// 30-day promise.
    /// </remarks>
    public bool ImmediatePurgeOnDelete { get; set; } = false;

    /* The per-session data encryption key, wrapped by a versioned master key from
       IContentKeyRing with "chatsession:<id>" as associated data.

       Rows are encrypted under this DEK rather than under the master key, so rotating the
       master re-wraps these three columns and rewrites no content at all. It also makes
       crypto-shredding one update: null these and the session's rows are unreadable whatever
       else survives. */

    /// <summary>The session's DEK, wrapped under <see cref="ContentKeyVersion"/>. Null until first use.</summary>
    [MaxLength(256)]
    public string? EncryptedContentKey { get; set; }

    [MaxLength(64)]
    public string? ContentKeyNonce { get; set; }

    [MaxLength(64)]
    public string? ContentKeyTag { get; set; }

    /// <summary>
    /// Which master key version wraps <see cref="EncryptedContentKey"/>.
    /// </summary>
    /// <remarks>
    /// The **key** version lives here, on the session. The row envelope's own <c>v1</c> is a
    /// **format** version. Keeping the key version out of the row is what lets rotation touch
    /// one row per session instead of every row of content.
    ///
    /// A session referencing a version no longer in the keyring cannot be read. Retiring a
    /// version while any session still names it is the one rotation mistake that loses data.
    /// </remarks>
    [MaxLength(32)]
    public string? ContentKeyVersion { get; set; }
}
