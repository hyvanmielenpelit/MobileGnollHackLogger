namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

public enum AccountDeletionRequestStatus : byte { Pending = 0, Completed = 1, Declined = 2 }

/// <summary>
/// A user's request to delete their account that was held for review. Not a foreign key to the user, so the
/// decision survives the deletion; once completed, the user id and name are cleared and only the decision remains.
/// </summary>
public class AccountDeletionRequest
{
    public long Id { get; set; }

    /// <summary>Cleared on completion.</summary>
    [MaxLength(450)]
    public string? AspNetUserId { get; set; }

    /// <summary>Cleared on completion.</summary>
    [MaxLength(256)]
    public string? UserNameSnapshot { get; set; }

    /// <summary>For the decision email; cleared on completion.</summary>
    [MaxLength(256)]
    public string? EmailSnapshot { get; set; }

    public DateTime RequestedUtc { get; set; }

    public AccountDeletionRequestStatus Status { get; set; }

    /// <summary>Why it was held, as the admin will read it: the manual hold's reason and/or the automatic rule's event summary.</summary>
    [MaxLength(2048)]
    public string HoldReasons { get; set; } = default!;

    [MaxLength(450)]
    public string? DecidedByUserId { get; set; }

    public DateTime? DecidedUtc { get; set; }

    [MaxLength(512)]
    public string? DecisionNote { get; set; }

    /// <summary>When the admin review email for this request was last sent; null until one is sent successfully.</summary>
    public DateTime? LastReviewEmailUtc { get; set; }
}
