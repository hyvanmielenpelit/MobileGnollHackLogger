namespace MobileGnollHackLogger.Data;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

/// <summary>The action a bulk operation applies. Stored as a short; the values are part of the stored contract.</summary>
public enum AdminBulkActionKind : short
{
    Delete = 1, LockOut = 2, Unlock = 3, Ban = 4, Unban = 5, SignOutEverywhere = 6,
}

/// <summary>The lifecycle of a bulk operation. Stored as a short; the values are part of the stored contract.</summary>
public enum AdminBulkOperationStatus : short
{
    Previewed = 0, Queued = 1, Running = 2, Completed = 3,
    CancelRequested = 4, Canceled = 5, Interrupted = 6, Expired = 7,
}

/// <summary>The outcome of one account in a bulk operation. Stored as a short.</summary>
public enum AdminBulkItemOutcome : short
{
    Pending = 0, Changed = 1, Skipped = 2, Failed = 3,
}

/// <summary>
/// A bulk administrator action on accounts: created by a preview, which fixes its item list and expires,
/// then queued and run one account at a time.
/// </summary>
public class AdminBulkOperation
{
    public long Id { get; set; }

    [MaxLength(450)]
    public string ActorUserId { get; set; } = default!;

    [MaxLength(256)]
    public string ActorUserName { get; set; } = default!;

    public AdminBulkActionKind Action { get; set; }

    /// <summary>The action's options: lock duration, reason.</summary>
    public string? OptionsJson { get; set; }

    /// <summary>The criteria the preview matched with; re-applied to each account before it is processed.</summary>
    public string? CriteriaJson { get; set; }

    public AdminBulkOperationStatus Status { get; set; }

    public DateTime CreatedUtc { get; set; }

    /// <summary>The preview cannot be executed after this.</summary>
    public DateTime ExpiresUtc { get; set; }

    public DateTime? StartedUtc { get; set; }

    public DateTime? CompletedUtc { get; set; }

    public int MatchedCount { get; set; }

    public int EligibleCount { get; set; }

    public int RefusedCount { get; set; }

    public int ChangedCount { get; set; }

    public int SkippedCount { get; set; }

    public int FailedCount { get; set; }

    /// <summary>True when the run included the accounts behind the Advanced disclosure.</summary>
    public bool IncludedAdvanced { get; set; }

    /// <summary>Per refusal reason, a count and up to 50 example user names.</summary>
    public string? RefusedSummaryJson { get; set; }

    public ICollection<AdminBulkOperationItem>? Items { get; set; }
}

/// <summary>One account of a bulk operation. Has no foreign key to the user, so it outlives a deleted account.</summary>
public class AdminBulkOperationItem
{
    public long Id { get; set; }

    public long OperationId { get; set; }
    public AdminBulkOperation Operation { get; set; } = default!;

    [MaxLength(450)]
    public string UserId { get; set; } = default!;

    [MaxLength(256)]
    public string UserName { get; set; } = default!;

    /// <summary>True for an account behind the Advanced disclosure (delete only).</summary>
    public bool IsAdvanced { get; set; }

    /// <summary>Excluded by the administrator before execution, or an advanced account not opted in.</summary>
    public bool Excluded { get; set; }

    public AdminBulkItemOutcome Outcome { get; set; }

    [MaxLength(512)]
    public string? Message { get; set; }

    [MaxLength(256)]
    public string? BeforeStatus { get; set; }

    [MaxLength(256)]
    public string? AfterStatus { get; set; }

    public DateTime? ProcessedUtc { get; set; }
}
