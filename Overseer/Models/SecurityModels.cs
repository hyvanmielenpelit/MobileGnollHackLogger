namespace Overseer.Models;

using System;
using System.Collections.Generic;

/// <summary>Body of an admin action that requires a written reason (block, deletion hold).</summary>
public class AdminReasonRequest
{
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Counts of one security event kind in a date range.</summary>
public class SecurityKindCountDto
{
    /// <summary>The <c>SecurityEventKind</c> name.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Sum of the runs' counts.</summary>
    public long Count { get; set; }

    public int DistinctUsers { get; set; }
    public int DistinctIps { get; set; }
}

public class SecuritySummaryDto
{
    public List<SecurityKindCountDto> Kinds { get; set; } = new();
    public int PendingDeletionRequests { get; set; }
    public int ActiveAccountHolds { get; set; }
}

public class SecurityEventDto
{
    public long Id { get; set; }
    public DateTime OccurredUtc { get; set; }
    public DateTime LastOccurredUtc { get; set; }

    /// <summary>The <c>SecurityEventKind</c> name.</summary>
    public string Kind { get; set; } = string.Empty;

    public int Count { get; set; }
    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public bool UserIsOverseerBlocked { get; set; }
    public string? IpAddress { get; set; }
    public long? SystemAiApiConfigurationId { get; set; }
    public string? Provider { get; set; }
    public string? ModelId { get; set; }
    public string? Code { get; set; }
    public string? Detail { get; set; }
}

public class SecurityEventsPageDto
{
    public int TotalCount { get; set; }
    public List<SecurityEventDto> Rows { get; set; } = new();
}

public class SafetyIdentifierLookupDto
{
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
}

public class AccountDeletionRequestDto
{
    public long Id { get; set; }

    /// <summary>Null once the request is completed.</summary>
    public string? UserId { get; set; }

    /// <summary>Null once the request is completed (shown as <i>Deleted account</i>).</summary>
    public string? UserName { get; set; }

    public DateTime RequestedUtc { get; set; }
    public int AgeDays { get; set; }

    /// <summary>Pending and at least <c>AccountHold:ReviewHighlightDays</c> old.</summary>
    public bool IsOverdue { get; set; }

    /// <summary>"Pending", "Completed" or "Declined".</summary>
    public string Status { get; set; } = "Pending";

    /// <summary>The reasons stored when the request was held.</summary>
    public string HoldReasons { get; set; } = string.Empty;

    /// <summary>The reasons as evaluated now; null when the request is decided or the account is gone.</summary>
    public string? CurrentReasons { get; set; }

    public bool ManualHold { get; set; }
    public bool AutomaticRule { get; set; }

    public string? DecidedByUserName { get; set; }
    public DateTime? DecidedUtc { get; set; }
    public string? DecisionNote { get; set; }
}

public class AccountDeletionRequestsPageDto
{
    public int TotalCount { get; set; }
    public int PendingCount { get; set; }
    public int OverdueCount { get; set; }

    /// <summary>False while DumpLogPath or ConversationsDataLocation is missing; Approve is refused then.</summary>
    public bool ErasurePathsConfigured { get; set; }

    /// <summary>Names the missing settings when <see cref="ErasurePathsConfigured"/> is false.</summary>
    public string? ErasurePathsMessage { get; set; }

    public List<AccountDeletionRequestDto> Rows { get; set; } = new();
}

public class ApproveAccountDeletionRequest
{
    public string ConfirmUserName { get; set; } = string.Empty;
}

public class DeclineAccountDeletionRequest
{
    public string Note { get; set; } = string.Empty;
}

public class AccountDeletionDecisionResultDto
{
    public bool EmailSent { get; set; }

    /// <summary>Why the decision email was not sent, when it was not.</summary>
    public string? EmailError { get; set; }

    /// <summary>Approve only: bones files whose rows were deleted but which were not found on disk.</summary>
    public int BonesFilesMissing { get; set; }

    /// <summary>Approve only: dumplog and attachment folders that were not found on disk.</summary>
    public int FoldersMissing { get; set; }
}
