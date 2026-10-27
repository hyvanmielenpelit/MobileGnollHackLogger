namespace MobileGnollHackLogger.Data;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>The kind of an administrator action on an account. Stored as a short; the values are part of the stored contract and are only appended.</summary>
public enum AdminUserActionKind : short
{
    LockedOut = 1, Unlocked = 2, Banned = 3, Unbanned = 4,
    GameRestrictionsChanged = 5, SignedOutEverywhere = 6, EmailConfirmed = 7, TwoFactorReset = 8,
    Deleted = 9,
    AiChatBlocked = 10, AiChatUnblocked = 11,
    HoldPlaced = 12, HoldReleased = 13,
    DeletionRequestApproved = 14, DeletionRequestDeclined = 15,
    EmailChanged = 16,
    GameSignInBlockCleared = 17,
}

/// <summary>
/// One administrator action on an account: the durable admin log. Has no foreign key to the user, so it
/// outlives a deleted account; the user names are copied in for that reason. Holds no email address.
/// </summary>
public class AdminUserAction
{
    public long Id { get; set; }

    public DateTime OccurredUtc { get; set; }

    [MaxLength(450)]
    public string ActorUserId { get; set; } = default!;

    [MaxLength(256)]
    public string ActorUserName { get; set; } = default!;

    [MaxLength(450)]
    public string SubjectUserId { get; set; } = default!;

    [MaxLength(256)]
    public string SubjectUserName { get; set; } = default!;

    public AdminUserActionKind Kind { get; set; }

    /// <summary>Admin-written; never shown to the user.</summary>
    [MaxLength(512)]
    public string? Reason { get; set; }

    /// <summary>The status before and after, or the counts a deletion erased.</summary>
    [MaxLength(1024)]
    public string? Detail { get; set; }

    /// <summary>Set when the action was one item of a bulk operation.</summary>
    public long? BulkOperationId { get; set; }
    public AdminBulkOperation? BulkOperation { get; set; }
}
